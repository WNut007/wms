namespace WMS.Domain.Entities.Inventory;

// Phase 13 (ADR-012) — TransferOrder line item.
//
// Owner preserved (3PL/VMI invariant). Lot preserved when set.
// Quantities flow Pending → Dispatched → Received | Variance.
//
// QtyLossInTransit is a PERSISTED computed column on the table —
// (ISNULL(QtyDispatched,0) - ISNULL(QtyReceived,0)). The entity
// exposes it as a settable property so Dapper can materialise it
// on read; never written by service code.
public sealed class TransferOrderLine
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public int LineNumber { get; set; }

    public Guid ProductId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? PalletId { get; set; }

    public Guid FromLocationId { get; set; }
    public Guid ToLocationId { get; set; }

    public decimal QtyRequested { get; set; }
    public decimal? QtyDispatched { get; set; }
    public decimal? QtyReceived { get; set; }
    public decimal QtyLossInTransit { get; set; }   // computed by DB

    public string Status { get; set; } = "Pending";
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
