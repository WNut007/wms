namespace WMS.Domain.Entities.Outbound;

// Phase 14B (ADR-005) — outbound.OrderAllocations. Per-line linkage
// from a SalesOrderLine to a specific inventory.Stock row, with the
// quantity reserved against that row.
//
// Status flow:
//   Active → Released   (cancel / short-pick reversal)        — 14B
//   Active → Picked     (consumed by a pick task submission)  — 14C
//
// Released and Picked are distinct terminal states. CK_OrderAllocations
// _AuditMatchesStatus enforces the per-state audit trio.
//
// Owner-aware (ADR-007 invariant): the (Product, Owner, UoM) on
// SalesOrderLineId must match the same tuple on StockId. Service
// layer enforces — no FK across the schemas.
public sealed class OrderAllocation : BaseEntity
{
    public Guid SalesOrderLineId { get; set; }
    public Guid StockId { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public string Status { get; set; } = "Active";

    public DateTime AllocatedAt { get; set; }
    public Guid? AllocatedBy { get; set; }

    public DateTime? ReleasedAt { get; set; }
    public Guid? ReleasedBy { get; set; }
    public string? ReleaseReason { get; set; }

    // Phase 14C — populated when allocation is consumed by a pick.
    public DateTime? PickedAt { get; set; }
    public Guid? PickedBy { get; set; }
}
