namespace WMS.DAL.Repositories.Counts;

// Phase 12 — read-projection for /CycleCounts list page. Resolved
// warehouse code + per-session line aggregates (LineCount / Counted /
// Variance counts). Mirrors PurchaseOrderListRow / ReceivingListRow.
public sealed record CycleCountListRow(
    Guid Id,
    string CountNumber,
    Guid WarehouseId,
    string WarehouseCode,
    string? LocationFilterCode,    // null = whole-warehouse
    string Status,
    int LineCount,
    int CountedLineCount,
    int VarianceLineCount,         // lines where Counted != Expected
    string StartedByName,
    DateTime StartedAt);

// Filter shape for ICycleCountRepository.GetPagedAsync.
public sealed record CycleCountFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,         // matches CountNumber
    string? Status = null,         // 'Counting'|'Review'|'Applied'|'Cancelled'
    string? WarehouseCode = null,
    string SortBy = "startedAt",
    bool SortDesc = true);

// Phase 12 — chip-count aggregate (Phase 10A pattern). Counts share
// search + warehouse filter; ignore status.
public sealed record CycleCountStatusCounts(
    int All,
    int Counting,
    int Review,
    int Applied,
    int Cancelled);
