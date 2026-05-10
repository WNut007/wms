namespace WMS.DAL.Repositories.Outbound;

// Phase 15A — read-projection for /PickTasks list page. JOINs
// outbound.SalesOrders for SoNumber + master.Customers for the
// customer label, plus per-task line aggregates (LineCount).
public sealed record PickTaskListRow(
    Guid Id,
    string PickNumber,
    Guid SalesOrderId,
    string SoNumber,
    string CustomerCode,
    string CustomerName,
    string Status,
    int LineCount,
    DateTime GeneratedAt,
    string GeneratedByName,
    DateTime? CompletedAt,
    DateTime? CancelledAt);

// Filter shape for IPickTaskRepository.GetPagedAsync.
public sealed record PickTaskFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,                // matches PickNumber OR SoNumber
    string? Status = null,                // 'Pending' | 'InProgress' | 'Picked' | 'PartiallyPicked' | 'Cancelled'
    string SortBy = "generatedAt",
    bool SortDesc = true);

// Phase 15A — chip-count aggregate for /PickTasks list. Counts
// respect Search filter; ignore Status so inactive chips still
// display their totals.
public sealed record PickTaskStatusCounts(
    int All,
    int Pending,
    int InProgress,
    int Picked,
    int PartiallyPicked,
    int Cancelled);
