namespace WMS.DAL.Repositories.Outbound;

// Phase 14A — read-projection for /SalesOrders list page. Carries
// resolved Customer code/name + Warehouse code, plus per-SO line
// aggregates (LineCount + TotalQuantity). Mirrors PurchaseOrderList-
// Row shape from Phase 9A.
public sealed record SalesOrderListRow(
    Guid Id,
    string SoNumber,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    Guid WarehouseId,
    string WarehouseCode,
    // DateTime? not DateOnly? for the same reason as PurchaseOrderList-
    // Row.ExpectedDate — record positional ctors bind strictly to the
    // SQL column type. SQL DATE materializes as DateTime. The entity
    // (class+setters) accepts DateOnly cleanly because Dapper's
    // per-property convertor does the bridge.
    DateTime OrderDate,
    DateTime? RequestedShipDate,
    string Status,
    int LineCount,
    decimal TotalQuantity,
    string CreatedByName,
    DateTime CreatedAt);

// Filter shape for ISalesOrderRepository.GetPagedAsync. Mirrors
// PurchaseOrderFilter.
public sealed record SalesOrderFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,                // matches SoNumber
    string? Status = null,                // 'Draft' | 'Open' | 'Cancelled'
    string? CustomerCode = null,
    string? WarehouseCode = null,
    string SortBy = "orderDate",
    bool SortDesc = true);

// Phase 14A — chip-count aggregate for /SalesOrders list. Counts
// respect Customer + Warehouse + Search filter; ignore Status so
// inactive chips still display their totals.
//
// Phase 14B added Allocating + Allocated counts.
// Phase 14C added Picking + Picked + PartiallyPicked counts.
// Phase 14D added Packed count. Order matches the workflow chronology
// so the chip strip renders left-to-right in pipeline order.
public sealed record SalesOrderStatusCounts(
    int All,
    int Draft,
    int Open,
    int Allocating,
    int Allocated,
    int Picking,
    int Picked,
    int PartiallyPicked,
    int Packed,
    int Cancelled);

// Phase 14A — read-projection for the Detail Lines tab. Resolved
// Product code/name + Owner code + UoM code via JOIN, single round-
// trip. Shape parallel to PurchaseOrderLineRow + TransferOrderLineRow.
//
// Phase 14B added: AllocatedQuantity (between OrderedQuantity and
// UnitPrice) — denormalized aggregate of the line's Active
// OrderAllocations rows.
// Phase 14C added: PickedQuantity (after AllocatedQuantity) —
// denormalized aggregate of the line's pick-task line PickedQty
// (bumped inside SubmitAsync's TX).
public sealed record SalesOrderLineRow(
    Guid Id,
    int LineNumber,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string OwnerCode,
    string UomCode,
    decimal OrderedQuantity,
    decimal AllocatedQuantity,
    decimal PickedQuantity,
    decimal? UnitPrice,
    string? Notes);
