namespace WMS.Domain.Entities.Inventory;

// Phase 13 (ADR-012) — inter-warehouse Transfer header.
//
// State machine (v1 — collapsed from ADR's 9-state):
//   Draft → Submitted → Approved → InTransit → Received  (terminal happy)
//   anywhere-before-InTransit → Cancelled                (terminal failure)
//   InTransit → Lost                                     (terminal failure)
//
// Per-state audit trio populates as the workflow advances;
// CK_TransferOrders_AuditMatchesStatus enforces the invariant in DB.
public sealed class TransferOrder : BaseEntity
{
    public string TransferNumber { get; set; } = "";
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public string Status { get; set; } = "Draft";
    public string Reason { get; set; } = "Other";
    public string? Notes { get; set; }

    public Guid RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }

    public Guid? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }

    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public Guid? DispatchedBy { get; set; }
    public DateTime? DispatchedAt { get; set; }

    public Guid? ReceivedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public Guid? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    public Guid? LostBy { get; set; }
    public DateTime? LostAt { get; set; }
    public string? LossReason { get; set; }
}
