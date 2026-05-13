namespace WMS.Domain.Entities.Inbound;

// Maps to inbound.PurchaseOrderLines. Receiving-δ updates
// ReceivedQuantity and flips Status as deliveries arrive; for now
// (γ) the line is created Open and stays that way.
public sealed class PurchaseOrderLine : BaseEntity
{
    public Guid PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public Guid UomId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public string Status { get; set; } = "Open";

    // Block 4.5.2 d.2.3.c — gap-10 sequence driving UI row order (drag-
    // drop reorder). Distinct from LineNumber which is the operator-
    // visible identity preserved by Option X (per CLAUDE.md d.2.3.b
    // entry). Backfilled by Migration_037 as LineNumber on existing
    // rows; new rows get explicit values from the controller's
    // reorder path.
    public int DisplayOrder { get; set; }
}
