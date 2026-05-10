namespace WMS.DAL.Repositories.Outbound;

// Phase 15A — read-projection for /Shipments list page. JOINs
// outbound.SalesOrders + master.Customers + per-shipment carton
// aggregate (count + total weight). Different shape than Pick/Pack
// list rows — no LineCount (Shipment is single-table, no lines),
// instead surfaces CartonCount + Carrier + Tracking.
public sealed record ShipmentListRow(
    Guid Id,
    string ShipmentNumber,
    Guid SalesOrderId,
    string SoNumber,
    string CustomerCode,
    string CustomerName,
    string Status,
    string? CarrierName,
    string? TrackingNumber,
    int CartonCount,
    DateTime GeneratedAt,
    string GeneratedByName,
    DateTime? ShippedAt,
    DateTime? CancelledAt);

// Filter shape for IShipmentRepository.GetPagedAsync.
public sealed record ShipmentFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,                // matches ShipmentNumber OR SoNumber OR TrackingNumber
    string? Status = null,                // 'Pending' | 'Shipped' | 'Cancelled'
    string SortBy = "generatedAt",
    bool SortDesc = true);

// Phase 15A — chip-count aggregate for /Shipments list. 3-state
// (mirrors the 14E 3-state shipment machine).
public sealed record ShipmentStatusCounts(
    int All,
    int Pending,
    int Shipped,
    int Cancelled);
