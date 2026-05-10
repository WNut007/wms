using WMS.Domain.Entities.Outbound;

namespace WMS.Web.Services.Outbound;

// Phase 17 (ADR-009) — pack video orchestration. Glue layer between
// the controller (multipart upload + range-less stream) and the two
// underlying systems: IDocumentStorageService (the bytes) +
// IPackVideoRepository (the metadata).
public interface IPackVideoService
{
    // Upload a recording. Validates pack-task state (Packed only —
    // Pending tasks have no carton yet, so video is meaningless),
    // writes the blob via storage, then INSERTs the PackVideo row.
    // Returns the new row's Id for the client to display
    // confirmation.
    //
    // No TX wrapping needed: storage write happens first; if the DB
    // insert fails, the orphan blob will eventually be cleaned by
    // the retention job (via the RecordedAt column being its
    // upload time → 10-day cutoff). Acceptable trade-off for MVP.
    Task<Guid> UploadAsync(
        Guid tenantId,
        Guid packTaskId,
        Stream content,
        string fileName,
        string contentType,
        int durationSec,
        Guid currentUserId,
        CancellationToken ct = default);

    // Fetch a stream + metadata for playback. Returns null when the
    // PackVideo row points at a missing storage row (e.g. retention
    // job ran between metadata read + storage read — unlikely race
    // but cleanly handled).
    Task<(Stream Stream, string ContentType, string FileName)?> GetStreamAsync(
        Guid tenantId,
        Guid packVideoId,
        CancellationToken ct = default);

    // Hard delete (admin / debug). Removes storage blob first,
    // then the PackVideo row. Idempotent — missing video returns
    // false.
    Task<bool> DeleteAsync(
        Guid tenantId,
        Guid packVideoId,
        CancellationToken ct = default);

    // Latest video for a pack task, or null. Used by the Pack
    // Detail UI to surface the "Watch video" link.
    Task<PackVideo?> GetLatestForPackTaskAsync(
        Guid tenantId,
        Guid packTaskId,
        CancellationToken ct = default);
}
