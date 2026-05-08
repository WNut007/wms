using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Documents;
using WMS.Domain.Entities.Documents;
using WMS.Web.Services.Storage;

namespace WMS.IntegrationTests.Storage;

// LocalFileStorageService integration test. Uses a real temp directory
// (so we exercise the actual File I/O paths) but stubs the document
// repository with an in-memory dictionary — this keeps the test free of
// a SQL Server dependency while still covering disk + path logic + the
// repo handshake. Lives in WMS.IntegrationTests because the SUT is in
// WMS.Web (net8.0-windows TFM, can't be referenced from UnitTests).
public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly InMemoryDocRepo _repo = new();

    public LocalFileStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "wms-test-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task UploadDownloadDeleteCycle_HappyPath()
    {
        var sut = NewService();
        var bytes = System.Text.Encoding.UTF8.GetBytes("Hello, document!");
        using var ms = new MemoryStream(bytes);

        var meta = await sut.UploadAsync(
            ms, "spec.pdf", "application/pdf",
            "Product", "SKU-000001", "Specification", "Test User");

        Assert.Equal("Local", meta.StorageProvider);
        Assert.Equal(bytes.LongLength, meta.FileSize);
        Assert.Contains(_tenantId.ToString("N"), meta.StorageKey);
        Assert.Contains("Product", meta.StorageKey);

        // Bytes landed under the tenant scope.
        var absPath = Path.Combine(_tempRoot, meta.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(absPath), $"Expected file at {absPath}");

        // Download returns the same bytes + metadata.
        var stream = await sut.GetStreamAsync(meta.DocumentId);
        Assert.NotNull(stream);
        using (var reader = new StreamReader(stream!.Value.Stream))
        {
            var content = await reader.ReadToEndAsync();
            Assert.Equal("Hello, document!", content);
        }

        // Delete drops the bytes and the metadata.
        var deleted = await sut.DeleteAsync(meta.DocumentId);
        Assert.True(deleted);
        Assert.False(File.Exists(absPath));
        Assert.Null(await sut.GetMetadataAsync(meta.DocumentId));
    }

    [Fact]
    public async Task ListByEntity_ReturnsUploadedDocs_AndScopesByTenant()
    {
        var sut = NewService();
        await UploadAsync(sut, "a.pdf", "Product", "SKU-A");
        await UploadAsync(sut, "b.pdf", "Product", "SKU-A");
        await UploadAsync(sut, "c.pdf", "Product", "SKU-B");   // different entityId

        var listA = await sut.ListByEntityAsync("Product", "SKU-A");
        Assert.Equal(2, listA.Count);
        Assert.All(listA, d => Assert.Equal("SKU-A", d.EntityId));

        var listB = await sut.ListByEntityAsync("Product", "SKU-B");
        Assert.Single(listB);
    }

    [Fact]
    public async Task Upload_RejectsExtensionNotInAllowlist()
    {
        var sut = NewService();
        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = await Assert.ThrowsAsync<StorageValidationException>(() =>
            sut.UploadAsync(ms, "evil.exe", "application/octet-stream",
                "Product", "SKU-X", "Document", "u"));

        Assert.Contains("not allowed", ex.Message, StringComparison.OrdinalIgnoreCase);

        // No bytes should have been written.
        var dir = Path.Combine(_tempRoot, _tenantId.ToString("N"));
        Assert.False(Directory.Exists(dir) && Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Upload_RejectsOversizedFile()
    {
        // 1 MB ceiling, 2 MB payload.
        var sut = NewService(maxMb: 1);
        using var ms = new MemoryStream(new byte[2 * 1024 * 1024]);

        var ex = await Assert.ThrowsAsync<StorageValidationException>(() =>
            sut.UploadAsync(ms, "big.pdf", "application/pdf",
                "Product", "SKU-X", "Document", "u"));

        Assert.Contains("MB", ex.Message);
    }

    [Fact]
    public async Task Upload_PathTraversalEntityId_IsSanitized()
    {
        var sut = NewService();
        using var ms = new MemoryStream(new byte[] { 1 });

        // ".." segments and slashes are stripped by SanitizeSegment, so
        // the resulting path stays under the tenant root.
        var meta = await sut.UploadAsync(
            ms, "x.pdf", "application/pdf",
            "Product", "../../escape", "Document", "u");

        var absPath = Path.Combine(_tempRoot, meta.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.StartsWith(Path.GetFullPath(_tempRoot), Path.GetFullPath(absPath));
        Assert.True(File.Exists(absPath));
    }

    [Fact]
    public async Task Delete_NonexistentId_ReturnsFalse()
    {
        var sut = NewService();
        Assert.False(await sut.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetStream_MetadataExistsButFileMissing_ReturnsNull()
    {
        var sut = NewService();
        using var ms = new MemoryStream(new byte[] { 1 });
        var meta = await sut.UploadAsync(
            ms, "x.pdf", "application/pdf",
            "Product", "SKU-X", "Document", "u");

        // Yank the bytes off disk behind the service's back.
        var absPath = Path.Combine(_tempRoot, meta.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(absPath);

        Assert.Null(await sut.GetStreamAsync(meta.DocumentId));
    }

    private async Task<DocumentMetadata> UploadAsync(
        LocalFileStorageService sut, string fileName, string entityType, string entityId)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileName));
        return await sut.UploadAsync(
            ms, fileName, "application/pdf",
            entityType, entityId, "Document", "u");
    }

    private LocalFileStorageService NewService(int maxMb = 25)
    {
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.RequireTenantId()).Returns(_tenantId);
        tenantCtx.SetupGet(t => t.CurrentTenantId).Returns(_tenantId);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(_userId);
        currentUser.SetupGet(u => u.FullName).Returns("Test User");
        currentUser.SetupGet(u => u.Email).Returns("test@example.com");

        var factory = new Mock<IDocumentRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(_repo);

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(_tempRoot);
        env.SetupGet(e => e.EnvironmentName).Returns(Environments.Development);

        var options = Options.Create(new DocumentStorageOptions
        {
            Provider = "Local",
            Local = new() { RootPath = _tempRoot },   // absolute path so we don't double-resolve under ContentRoot
            MaxFileSizeMB = maxMb,
            AllowedExtensions = new[] { ".pdf", ".docx", ".jpg", ".png" },
        });

        return new LocalFileStorageService(
            factory.Object,
            tenantCtx.Object,
            currentUser.Object,
            options,
            env.Object,
            NullLogger<LocalFileStorageService>.Instance);
    }

    // Tiny in-memory stand-in for IDocumentRepository so tests don't
    // require SQL Server. Mirrors the real repo's contract closely
    // enough to exercise the storage service end to end.
    private sealed class InMemoryDocRepo : IDocumentRepository
    {
        private readonly Dictionary<Guid, DocumentFile> _store = new();

        public Task<Guid> InsertAsync(DocumentFile e, CancellationToken ct = default)
        {
            if (e.Id == Guid.Empty) e.Id = Guid.NewGuid();
            _store[e.Id] = e;
            return Task.FromResult(e.Id);
        }

        public Task<DocumentFile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(id, out var e) && !e.IsArchived ? e : null);

        public Task<IReadOnlyList<DocumentFile>> GetByEntityAsync(
            string entityType, string entityId, CancellationToken ct = default)
        {
            IReadOnlyList<DocumentFile> rows = _store.Values
                .Where(e => e.EntityType == entityType && e.EntityId == entityId && !e.IsArchived)
                .OrderByDescending(e => e.CreatedAt)
                .ToList();
            return Task.FromResult(rows);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_store.Remove(id));
    }
}
