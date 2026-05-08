using Microsoft.Extensions.Options;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Documents;
using WMS.Domain.Entities.Documents;

namespace WMS.Web.Services.Storage;

// Production-grade storage backed by the local filesystem + documents.Files
// for metadata. Tenant-scoped: the resolved disk path always sits under
// {RootPath}/{tenantId}/... so an accidental cross-tenant read is a
// directory-traversal away rather than one missing WHERE clause.
//
// Path layout:  {RootPath}/{tenantId:N}/{entityType}/{entityId}/{fileId}{ext}
// StorageKey:   {tenantId:N}/{entityType}/{entityId}/{fileId}{ext}   (relative)
//
// The on-disk filename is the document Guid + extension, never the
// original filename, so collisions and rename races are impossible.
// Original filename is preserved on the metadata row for the download
// Content-Disposition.
//
// Validation runs BEFORE bytes are written to disk: extension allowlist
// (case-insensitive), MaxFileSizeMB, non-empty stream. Anything beyond
// that (virus scan, EXIF strip, image re-encode) is Phase 5+.
public sealed class LocalFileStorageService : IDocumentStorageService
{
    private readonly IDocumentRepositoryFactory _repoFactory;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly DocumentStorageOptions _options;
    private readonly ILogger<LocalFileStorageService> _logger;
    private readonly string _rootPath;
    private readonly HashSet<string> _allowedExt;

    public LocalFileStorageService(
        IDocumentRepositoryFactory repoFactory,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IOptions<DocumentStorageOptions> options,
        IWebHostEnvironment env,
        ILogger<LocalFileStorageService> logger)
    {
        _repoFactory = repoFactory;
        _tenant = tenant;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;

        _rootPath = ResolveRoot(_options.Local.RootPath, env.ContentRootPath);
        Directory.CreateDirectory(_rootPath);

        _allowedExt = new HashSet<string>(
            _options.AllowedExtensions.Select(e => e.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<DocumentMetadata> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string entityType,
        string entityId,
        string category,
        string uploadedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new StorageValidationException("File name is required.");
        if (string.IsNullOrWhiteSpace(entityType))
            throw new StorageValidationException("Entity type is required.");
        if (string.IsNullOrWhiteSpace(entityId))
            throw new StorageValidationException("Entity id is required.");

        var tenantId = _tenant.RequireTenantId();
        var safeEntityType = SanitizeSegment(entityType);
        var safeEntityId = SanitizeSegment(entityId);

        var ext = (Path.GetExtension(fileName) ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !_allowedExt.Contains(ext))
            throw new StorageValidationException(
                $"Extension '{ext}' is not allowed. " +
                $"Allowed: {string.Join(", ", _options.AllowedExtensions)}.");

        var maxBytes = (long)_options.MaxFileSizeMB * 1024 * 1024;
        if (content.CanSeek && content.Length > maxBytes)
            throw new StorageValidationException(
                $"File exceeds {_options.MaxFileSizeMB} MB limit.");

        var documentId = Guid.NewGuid();
        var relPath = Path.Combine(
            tenantId.ToString("N"),
            safeEntityType,
            safeEntityId,
            $"{documentId:N}{ext}");
        var absPath = Path.Combine(_rootPath, relPath);

        // Defence in depth — if SanitizeSegment misses something, the
        // resolved path could escape _rootPath. Reject before writing.
        var fullRoot = Path.GetFullPath(_rootPath);
        var fullAbs = Path.GetFullPath(absPath);
        if (!fullAbs.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new StorageValidationException("Resolved path escapes storage root.");

        Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);

        long writtenBytes;
        await using (var fs = new FileStream(absPath, FileMode.CreateNew, FileAccess.Write))
        {
            // CopyToAsync respects the cancellation token. We measure the
            // post-write length rather than trusting Stream.Length so a
            // misbehaving client that sends fewer/more bytes than declared
            // is recorded honestly.
            await content.CopyToAsync(fs, ct);
            writtenBytes = fs.Length;
        }

        // Re-check size against the streamed length too — protects
        // against clients with non-seekable streams that bypass the
        // pre-check above.
        if (writtenBytes > maxBytes)
        {
            File.Delete(absPath);
            throw new StorageValidationException(
                $"File exceeds {_options.MaxFileSizeMB} MB limit.");
        }

        var entity = new DocumentFile
        {
            Id = documentId,
            EntityType = entityType,
            EntityId = entityId,
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType,
            FileSize = writtenBytes,
            Extension = ext,
            Category = string.IsNullOrWhiteSpace(category) ? "Document" : category,
            StorageProvider = "Local",
            StorageKey = relPath.Replace('\\', '/'),
            IsArchived = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.UserId,  // null OK — see ICurrentUser docs
            Version = 0,
        };

        try
        {
            var repo = _repoFactory.For(tenantId);
            await repo.InsertAsync(entity, ct);
        }
        catch
        {
            // Roll back the on-disk write so we never leave orphan bytes
            // when the metadata insert fails (FK violation, duplicate, etc.).
            TryDelete(absPath);
            throw;
        }

        _logger.LogInformation(
            "Uploaded document {DocumentId} ({FileName}, {Bytes} B) for {EntityType}/{EntityId} tenant {TenantId}",
            documentId, fileName, writtenBytes, entityType, entityId, tenantId);

        return ToMetadata(entity, uploadedBy);
    }

    public async Task<DocumentMetadata?> GetMetadataAsync(
        Guid documentId, CancellationToken ct = default)
    {
        var tenantId = _tenant.RequireTenantId();
        var entity = await _repoFactory.For(tenantId).GetByIdAsync(documentId, ct);
        return entity is null ? null : ToMetadata(entity, "");
    }

    public async Task<(Stream Stream, DocumentMetadata Metadata)?> GetStreamAsync(
        Guid documentId, CancellationToken ct = default)
    {
        var tenantId = _tenant.RequireTenantId();
        var entity = await _repoFactory.For(tenantId).GetByIdAsync(documentId, ct);
        if (entity is null) return null;

        var absPath = Path.Combine(_rootPath, entity.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absPath))
        {
            _logger.LogWarning(
                "Document {DocumentId} metadata exists but bytes missing at {Path}",
                documentId, absPath);
            return null;
        }

        Stream stream = new FileStream(
            absPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        return (stream, ToMetadata(entity, ""));
    }

    public async Task<bool> DeleteAsync(Guid documentId, CancellationToken ct = default)
    {
        var tenantId = _tenant.RequireTenantId();
        var repo = _repoFactory.For(tenantId);
        var entity = await repo.GetByIdAsync(documentId, ct);
        if (entity is null) return false;

        // Delete bytes first — if the row delete fails afterwards we
        // leave a metadata row pointing at nothing, which surfaces as a
        // 404 on download (logged), and a follow-up retry succeeds.
        // Reverse order would orphan disk bytes nobody can find.
        var absPath = Path.Combine(_rootPath, entity.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        TryDelete(absPath);

        return await repo.DeleteAsync(documentId, ct);
    }

    public async Task<List<DocumentMetadata>> ListByEntityAsync(
        string entityType, string entityId, CancellationToken ct = default)
    {
        var tenantId = _tenant.RequireTenantId();
        var rows = await _repoFactory.For(tenantId)
            .GetByEntityAsync(entityType, entityId, ct);
        return rows.Select(r => ToMetadata(r, "")).ToList();
    }

    private static DocumentMetadata ToMetadata(DocumentFile e, string uploadedBy) =>
        new()
        {
            DocumentId = e.Id,
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            FileName = e.FileName,
            ContentType = e.ContentType,
            FileSize = e.FileSize,
            Category = e.Category,
            StorageProvider = e.StorageProvider,
            StorageKey = e.StorageKey,
            UploadedBy = string.IsNullOrEmpty(uploadedBy) ? "" : uploadedBy,
            UploadedAt = e.CreatedAt,
            IsArchived = e.IsArchived,
        };

    private static string ResolveRoot(string configured, string contentRoot) =>
        Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(contentRoot, configured));

    // Keep path segments to a known-safe vocabulary. EntityIds are
    // business keys (SKU, code) which already use letters/digits and
    // dashes; this strips anything else (slashes, '..', spaces) so the
    // resolved path can't escape upward.
    private static string SanitizeSegment(string raw)
    {
        var clean = new string(raw.Where(c =>
            char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        if (clean.Length == 0 || clean is "." or "..")
            throw new StorageValidationException(
                $"Invalid path segment: '{raw}'.");
        return clean;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file at {Path}", path);
        }
    }
}
