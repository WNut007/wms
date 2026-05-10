namespace WMS.Domain.Entities.Outbound;

// Phase 14A — outbound.SalesOrderLines.
//
// Owner preserved per ADR-007 (3PL/VMI invariant). Phase 14B's
// allocation matches Stock on the (Product, Owner, UoM) shape —
// preventing accidental commingling of one customer's owner-keyed
// stock with another's.
//
// Phase 14B added: AllocatedQuantity. Denormalized aggregate of the
// row's Active OrderAllocations rows. Stays in sync with the linking
// table inside the same TX as AllocateAsync / CancelAsync. CHECK
// 0 <= AllocatedQuantity <= OrderedQuantity. Pick/Ship qty fields
// arrive in 14C/D.
public sealed class SalesOrderLine : BaseEntity
{
    public Guid SalesOrderId { get; set; }
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Notes { get; set; }
}
