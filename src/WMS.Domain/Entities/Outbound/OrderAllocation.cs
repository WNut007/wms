namespace WMS.Domain.Entities.Outbound;

// Phase 14B (ADR-005) — outbound.OrderAllocations. Per-line linkage
// from a SalesOrderLine to a specific inventory.Stock row, with the
// quantity reserved against that row.
//
// Status flow (MVP):
//   Active   → Released         (terminal — cancel / short-pick reversal)
//
// Pick / Shipped states arrive in 14C/D — the audit-invariant CHECK
// will widen at that point.
//
// Audit pattern: AllocatedBy/At populated on insert (Active state);
// ReleasedBy/At populated when reversed. CK_OrderAllocations_Audit-
// MatchesStatus enforces the invariant.
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
}
