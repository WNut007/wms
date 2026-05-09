namespace WMS.DAL.Repositories.Inbound;

// Read-projection for the /PurchaseOrders list page. Carries
// resolved Owner code/name + Warehouse code, plus per-PO aggregates
// (LineCount, TotalExpectedQty, TotalReceivedQty) computed in SQL so
// the page renders from a single round-trip without per-row queries.
//
// Distinct from PurchaseOrderDetail (which is header + full line
// list); list page only needs row-level summaries.
public sealed record PurchaseOrderListRow(
    Guid Id,
    string PoNumber,
    Guid OwnerId,
    string OwnerCode,
    string OwnerName,
    Guid WarehouseId,
    string WarehouseCode,
    DateTime? ExpectedDate,
    string Status,
    int LineCount,
    decimal TotalExpectedQty,
    decimal TotalReceivedQty,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// Filter shape for IPurchaseOrderRepository.GetPagedAsync. Mirror
// the Phase 6B filter pattern (ProductFilter etc.).
public sealed record PurchaseOrderFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    string? Status = null,        // DB-canonical PascalCase: 'Open'|'Receiving'|'Closed'|'Cancelled'
    string? OwnerCode = null,
    string? WarehouseCode = null,
    string SortBy = "expectedDate",
    bool SortDesc = false);
