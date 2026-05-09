namespace WMS.DAL.Repositories.Inventory;

// Phase 11A — read-projection for /Adjustments list. JOIN-resolved
// product / location / warehouse codes + requester display name. Same
// separation as PurchaseOrderListRow vs entity.
public sealed record AdjustmentListRow(
    Guid Id,
    string AdjustmentNumber,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid WarehouseId,
    string WarehouseCode,
    Guid LocationId,
    string LocationCode,
    Guid OwnerId,
    string OwnerCode,
    string UomCode,
    decimal QuantityDelta,
    string Reason,
    string Status,
    string RequestedByName,
    DateTime RequestedAt);

// Filter shape for IAdjustmentRepository.GetPagedAsync.
public sealed record AdjustmentFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,        // matches AdjustmentNumber OR Product.Code
    string? Status = null,        // 'Pending'|'Applied'|'Rejected'
    string? Reason = null,        // closed-list reason code
    string? WarehouseCode = null,
    string SortBy = "requestedAt",
    bool SortDesc = true);

// Phase 11A — chip-count aggregate (mirrors Phase 10A pattern). Counts
// share search + warehouse + reason filter; ignore status.
public sealed record AdjustmentStatusCounts(
    int All,
    int Pending,
    int Applied,
    int Rejected);
