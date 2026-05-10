namespace WMS.DAL.Repositories.Inventory;

// Phase 13 — read-projection for /Transfers list. Resolved warehouse
// codes + per-transfer line aggregates (LineCount + total Requested
// + total Dispatched + total Received). Mirrors PurchaseOrderListRow
// shape.
public sealed record TransferOrderListRow(
    Guid Id,
    string TransferNumber,
    Guid FromWarehouseId,
    string FromWarehouseCode,
    Guid ToWarehouseId,
    string ToWarehouseCode,
    string Status,
    string Reason,
    int LineCount,
    decimal TotalRequested,
    decimal TotalDispatched,
    decimal TotalReceived,
    string RequestedByName,
    DateTime RequestedAt);

// Filter shape for ITransferOrderRepository.GetPagedAsync.
public sealed record TransferOrderFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,         // matches TransferNumber
    string? Status = null,         // 'Draft'|'Submitted'|'Approved'|'InTransit'|'Received'|'Cancelled'|'Lost'
    string? FromWarehouseCode = null,
    string? ToWarehouseCode = null,
    string SortBy = "requestedAt",
    bool SortDesc = true);

// Phase 13 — chip-count aggregate. Counts respect search +
// warehouse filter; ignore status.
public sealed record TransferOrderStatusCounts(
    int All,
    int Draft,
    int Submitted,
    int Approved,
    int InTransit,
    int Received,
    int Cancelled,
    int Lost);

// Read-projection for the Detail Lines table — resolved Product +
// FromLocation + ToLocation + UoM + Owner codes/names + Lot/Pallet
// numbers via LEFT JOIN.
public sealed record TransferOrderLineRow(
    Guid Id,
    int LineNumber,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid FromLocationId,
    string FromLocationCode,
    Guid ToLocationId,
    string ToLocationCode,
    string UomCode,
    string OwnerCode,
    string? LotNumber,
    string? PalletNumber,
    decimal QtyRequested,
    decimal? QtyDispatched,
    decimal? QtyReceived,
    decimal QtyLossInTransit,
    string Status,
    string? Notes);
