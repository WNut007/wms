namespace WMS.Domain.Entities.Counts;

// Phase 12 — cycle count session header. Maps to counts.CycleCounts.
//
// Workflow: Counting → Review → Applied; Cancelled is reachable from
// any non-Applied state. Apply atomically transitions Review →
// Applied + writes per-line Stock + Movement Log entries (MovementType
// =Cycle) inside a TransactionScope.
//
// LocationFilter null = whole-warehouse scope; set = single Location.
// Future: ProductFilter, ABC class, scheduled.
public sealed class CycleCount : BaseEntity
{
    public string CountNumber { get; set; } = "";
    public Guid WarehouseId { get; set; }
    public Guid? LocationFilter { get; set; }
    public string Status { get; set; } = "Counting";
    public string? Notes { get; set; }

    public Guid StartedBy { get; set; }
    public DateTime StartedAt { get; set; }

    public Guid? CountedBy { get; set; }
    public DateTime? CountedAt { get; set; }

    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? AppliedAt { get; set; }

    public Guid? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
}
