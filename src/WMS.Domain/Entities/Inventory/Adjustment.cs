namespace WMS.Domain.Entities.Inventory;

// Phase 11A (ADR-013) — General Stock Adjustment header. Single-line
// per design; cycle-count discrepancies use the future
// counts.CountAdjustments table (separate per ADR).
//
// Workflow is flat: Pending → (Applied | Rejected). Apply happens
// atomically as part of approval (no intermediate Approved state).
// Audit fields populate as the workflow advances; CHECK_AuditMatchesStatus
// in the DB enforces the per-status invariant.
//
// 6-tuple stock target (LocationId / ProductId / LotId / PalletId /
// OwnerId / UomId + WarehouseId for filter). StockId is null on Pending
// when the adjustment will create a new Stock row at the key; populated
// to the resolved row on Apply.
public sealed class Adjustment : BaseEntity
{
    public string AdjustmentNumber { get; set; } = "";

    public Guid? StockId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? PalletId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }
    public Guid WarehouseId { get; set; }

    public decimal QuantityDelta { get; set; }
    public string Reason { get; set; } = "";
    public string? Notes { get; set; }

    public string Status { get; set; } = "Pending";

    public Guid RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public Guid? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
}
