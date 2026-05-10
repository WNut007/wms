namespace WMS.Domain.Entities.Outbound;

// Phase 14A — outbound.SalesOrders header. MVP foundation only:
// status flow trimmed to Draft → Open with Cancelled as the failure
// terminal. Allocation / Pick / Pack / Ship states land in 14B+ via
// schema ALTER + a CK widening.
//
// Status is a string (constrained by the table's CHECK) rather than
// an enum — services that mutate Status carry the literal, no
// translation layer between Domain and SQL.
public sealed class SalesOrder : BaseEntity
{
    public string SoNumber { get; set; } = "";
    public Guid CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateOnly OrderDate { get; set; }
    public DateOnly? RequestedShipDate { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
}
