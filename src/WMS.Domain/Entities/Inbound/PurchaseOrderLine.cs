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
}
