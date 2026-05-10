namespace WMS.Domain.Entities.Outbound;

// Phase 14A — outbound.SalesOrderLines.
//
// Owner preserved per ADR-007 (3PL/VMI invariant). When Phase 14B's
// allocation runs, Stock matches on the (Product, Owner, UoM) shape —
// preventing accidental commingling of one customer's owner-keyed
// stock with another's.
//
// Allocation/pick/ship qty fields deliberately omitted from MVP; they
// arrive in 14B's schema ALTER + this entity's growth.
public sealed class SalesOrderLine : BaseEntity
{
    public Guid SalesOrderId { get; set; }
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Notes { get; set; }
}
