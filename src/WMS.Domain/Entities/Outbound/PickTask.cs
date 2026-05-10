namespace WMS.Domain.Entities.Outbound;

// Phase 14C — outbound.PickTasks header.
//
// State flow:
//   Pending → InProgress → Picked | PartiallyPicked | Cancelled
//
// One task per SO; lines snapshot the SO's Active OrderAllocations at
// generation time. Cancel from Pending/InProgress releases the
// allocations back to Active (allocation reservations restored to
// the available pool).
//
// Status as string per project convention; CK_PickTasks_Status
// constrains the allowed values + CK_PickTasks_AuditMatchesStatus
// enforces the per-state audit trio.
public sealed class PickTask : BaseEntity
{
    public string PickNumber { get; set; } = "";
    public Guid SalesOrderId { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? AssignedTo { get; set; }
    public string? Notes { get; set; }

    // Per-state audit trio. GeneratedBy/At always set on insert.
    public DateTime GeneratedAt { get; set; }
    public Guid? GeneratedBy { get; set; }

    public DateTime? StartedAt { get; set; }
    public Guid? StartedBy { get; set; }

    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }

    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
}
