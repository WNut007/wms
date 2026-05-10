namespace WMS.Domain.Entities.Outbound;

// Phase 17 (ADR-009) — outbound.PackVideos. Pack-specific metadata
// pointing at a documents.Files row (the actual blob).
//
// One PackVideo row per recording. PackTask can have multiple takes
// (operator records, doesn't like it, records again). Per-task
// playback uses the latest by RecordedAt; older takes stay in
// documents.Files until the retention job deletes them.
//
// No Version (recordings are appended, never edited; same convention
// as PackTaskLines + Cartons).
public sealed class PackVideo
{
    public Guid Id { get; set; }
    public Guid PackTaskId { get; set; }
    public Guid DocumentFileId { get; set; }
    public int DurationSec { get; set; }

    public DateTime RecordedAt { get; set; }
    public Guid? RecordedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
