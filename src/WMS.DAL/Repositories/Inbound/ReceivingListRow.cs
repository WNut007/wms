namespace WMS.DAL.Repositories.Inbound;

// Read-projection for the /Receiving list page. Mirrors
// PurchaseOrderListRow's shape — natural-key codes resolved + per-
// header aggregates computed in SQL so the page renders from a
// single round-trip.
//
// Note: PoNumber + OwnerCode/Name are nullable because Receiving
// supports blind receipts (PurchaseOrderId is nullable on the
// header). LEFT JOINs in the read SQL handle this.
public sealed record ReceivingListRow(
    Guid Id,
    string ReceivingNumber,
    Guid? PurchaseOrderId,
    string? PoNumber,
    string? VendorCode,
    string? VendorName,
    Guid WarehouseId,
    string WarehouseCode,
    DateTime ReceivedAt,
    string Status,
    int LineCount,
    decimal TotalReceivedQty,
    DateTime CreatedAt);

// Filter shape for IReceivingHeaderRepository.GetPagedAsync.
public sealed record ReceivingFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,           // matches ReceivingNumber OR PoNumber
    string? Status = null,           // 'Draft'|'Posted'|'Cancelled'
    string? WarehouseCode = null,
    string SortBy = "receivedAt",
    bool SortDesc = true);

// Phase 10A (TD-028) — chip-count aggregate for /Receiving list.
// Counts share the search + warehouse filter; status is excluded so
// the inactive chips display their per-status totals.
public sealed record ReceivingStatusCounts(
    int All,
    int Draft,
    int Posted,
    int Cancelled);
