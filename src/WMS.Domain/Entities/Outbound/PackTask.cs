namespace WMS.Domain.Entities.Outbound;

// Phase 14D — outbound.PackTasks header.
//
// State flow:
//   Pending → Packed | Cancelled
//
// One task per SO. Lines snapshot the SO's positively-picked lines
// (line.PickedQuantity > 0) at generation. Pre-Submit Cancel returns
// the SO to its prior state (Picked or PartiallyPicked).
//
// Status as string per project convention; CK_PackTasks_Status
// constrains the allowed values + CK_PackTasks_AuditMatchesStatus
// enforces the per-state audit trio.
public sealed class PackTask : BaseEntity
{
    public string PackNumber { get; set; } = "";
    public Guid SalesOrderId { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? AssignedTo { get; set; }
    public string? Notes { get; set; }

    // Per-state audit trio. GeneratedBy/At always set on insert.
    public DateTime GeneratedAt { get; set; }
    public Guid? GeneratedBy { get; set; }

    public DateTime? PackedAt { get; set; }
    public Guid? PackedBy { get; set; }

    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
}
