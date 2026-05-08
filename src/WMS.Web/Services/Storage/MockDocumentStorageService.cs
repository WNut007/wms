namespace WMS.Web.Services.Storage;

public class MockDocumentStorageService : IDocumentStorageService
{
    private static readonly Dictionary<Guid, DocumentMetadata> _store = new();
    private static readonly object _lock = new();
    private static bool _seeded = false;

    public Task<DocumentMetadata> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string entityType,
        string entityId,
        string category,
        string uploadedBy,
        CancellationToken ct = default)
    {
        EnsureSeeded();

        var meta = new DocumentMetadata
        {
            DocumentId = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            FileName = fileName,
            ContentType = contentType,
            FileSize = content.CanSeek ? content.Length : 100_000,
            Category = category,
            StorageProvider = "mock",
            StorageKey = $"mock/{entityType}/{entityId}/{fileName}",
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow
        };

        lock (_lock) { _store[meta.DocumentId] = meta; }
        return Task.FromResult(meta);
    }

    public Task<DocumentMetadata?> GetMetadataAsync(Guid documentId, CancellationToken ct = default)
    {
        EnsureSeeded();
        lock (_lock)
        {
            return Task.FromResult(_store.TryGetValue(documentId, out var m) ? m : null);
        }
    }

    public Task<(Stream Stream, DocumentMetadata Metadata)?> GetStreamAsync(
        Guid documentId, CancellationToken ct = default)
    {
        EnsureSeeded();
        DocumentMetadata? meta;
        lock (_lock) { _store.TryGetValue(documentId, out meta); }
        if (meta is null)
            return Task.FromResult<(Stream, DocumentMetadata)?>(null);

        // No real bytes — return a tiny placeholder so callers can still
        // exercise the download path in tests that wire the Mock provider.
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            $"Mock content for {meta.FileName}");
        return Task.FromResult<(Stream, DocumentMetadata)?>(
            (new MemoryStream(bytes), meta));
    }

    public Task<bool> DeleteAsync(Guid documentId, CancellationToken ct = default)
    {
        EnsureSeeded();
        lock (_lock) { return Task.FromResult(_store.Remove(documentId)); }
    }

    public Task<List<DocumentMetadata>> ListByEntityAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default)
    {
        EnsureSeeded();
        lock (_lock)
        {
            var list = _store.Values
                .Where(m => m.EntityType == entityType && m.EntityId == entityId && !m.IsArchived)
                .OrderByDescending(m => m.UploadedAt)
                .ToList();
            return Task.FromResult(list);
        }
    }

    private void EnsureSeeded()
    {
        if (_seeded) return;
        lock (_lock)
        {
            if (_seeded) return;
            SeedMockData();
            _seeded = true;
        }
    }

    private static void SeedMockData()
    {
        var seed = new (string EntityType, string EntityId, string FileName, string ContentType, string Category, long Size, int HoursAgo)[]
        {
            ("Product", "SKU-000001", "MacBook_Pro_14_Specifications.pdf", "application/pdf", "Specification", 1_245_000L, -2 * 24),
            ("Product", "SKU-000001", "User_Manual_EN.pdf", "application/pdf", "Manual", 856_000L, -3 * 24),
            ("Product", "SKU-000001", "Pricing_Tiers_2026.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Pricing", 412_000L, -7 * 24),
            ("Product", "SKU-000001", "CE_Certificate_2025.pdf", "application/pdf", "Certificate", 320_000L, -14 * 24),
            ("Warehouse", "WH-MAIN", "Lease_Agreement_2025.pdf", "application/pdf", "Contract", 2_100_000L, -30 * 24),
            ("Warehouse", "WH-MAIN", "Floor_Plan.pdf", "application/pdf", "Specification", 1_800_000L, -45 * 24),
            ("Customer", "CUS-0001", "Customer_Agreement.pdf", "application/pdf", "Contract", 980_000L, -60 * 24),
        };

        foreach (var item in seed)
        {
            var meta = new DocumentMetadata
            {
                DocumentId = Guid.NewGuid(),
                EntityType = item.EntityType,
                EntityId = item.EntityId,
                FileName = item.FileName,
                ContentType = item.ContentType,
                FileSize = item.Size,
                Category = item.Category,
                StorageProvider = "mock",
                StorageKey = $"mock/{item.EntityType}/{item.EntityId}/{item.FileName}",
                UploadedBy = "System Admin",
                UploadedAt = DateTime.UtcNow.AddHours(item.HoursAgo)
            };
            _store[meta.DocumentId] = meta;
        }
    }
}
