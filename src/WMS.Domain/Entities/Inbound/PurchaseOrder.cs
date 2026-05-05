namespace WMS.Domain.Entities.Inbound;

// Maps to inbound.PurchaseOrders. Status is a string (constrained by
// the table's CHECK to Open / Receiving / Closed / Cancelled) rather
// than an enum so the data layer doesn't need a translation step;
// services that mutate Status carry the literal.
public sealed class PurchaseOrder : BaseEntity
{
    public string PoNumber { get; set; } = "";
    public Guid OwnerId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public string Status { get; set; } = "Open";
    public string? Notes { get; set; }
}
