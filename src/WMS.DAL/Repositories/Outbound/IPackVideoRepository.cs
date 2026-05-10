using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 17 (ADR-009) — tenant-scoped persistence for outbound.PackVideos.
// Surface is small: insert on upload, lookup by task for playback,
// list-old-then-delete for the retention job.
public interface IPackVideoRepository
{
    // INSERT a single video row. Caller pre-assigns Id; the matching
    // documents.Files row already exists (UploadedAsync wrote it
    // first via IDocumentStorageService).
    Task CreateAsync(
        PackVideo video, Guid? userId, CancellationToken ct = default);

    // Latest take for a pack task. Returns null when no recording
    // exists yet. Phase 17 UI surfaces only the latest.
    Task<PackVideo?> GetLatestByPackTaskAsync(
        Guid packTaskId, CancellationToken ct = default);

    // Single-row lookup by Id — used by the playback endpoint to
    // resolve PackTaskId for permission scoping + DocumentFileId
    // for the storage stream.
    Task<PackVideo?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Phase 17 retention job — every video older than cutoff. Returns
    // both the PackVideo Id (for the row deletion) and the
    // DocumentFileId (for the storage deletion). Job iterates,
    // deletes per-video.
    Task<IReadOnlyList<RetainedVideoRow>> GetOlderThanAsync(
        DateTime cutoff, CancellationToken ct = default);

    // Hard-delete a single row. Caller has already deleted the
    // underlying documents.Files row + on-disk bytes.
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// Phase 17 retention job projection — the two columns the job needs
// per video (PackVideo PK + the FK to delete from storage).
public sealed record RetainedVideoRow(
    Guid Id,
    Guid DocumentFileId,
    DateTime RecordedAt);

public interface IPackVideoRepositoryFactory
{
    IPackVideoRepository For(Guid tenantId);
}
