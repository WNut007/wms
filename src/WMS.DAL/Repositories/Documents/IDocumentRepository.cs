using WMS.Domain.Entities.Documents;

namespace WMS.DAL.Repositories.Documents;

// Tenant-scoped reader/writer for documents.Files. Bound to one tenant
// connection per instance via the factory — services don't pass tenantId
// to every call.
public interface IDocumentRepository
{
    Task<Guid> InsertAsync(DocumentFile entity, CancellationToken ct = default);

    Task<DocumentFile?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentFile>> GetByEntityAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default);

    // Hard delete — the disk bytes are removed first by the storage
    // service, then this row. Use the IsArchived flag (Phase 5+) for
    // soft-hide semantics; this is the "user clicked delete" path.
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
