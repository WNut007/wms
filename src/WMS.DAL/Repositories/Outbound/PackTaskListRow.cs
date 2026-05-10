namespace WMS.DAL.Repositories.Outbound;

// Phase 15A — read-projection for /PackTasks list page. JOINs
// outbound.SalesOrders + master.Customers + per-task line aggregate.
// Mirrors PickTaskListRow shape.
public sealed record PackTaskListRow(
    Guid Id,
    string PackNumber,
    Guid SalesOrderId,
    string SoNumber,
    string CustomerCode,
    string CustomerName,
    string Status,
    int LineCount,
    DateTime GeneratedAt,
    string GeneratedByName,
    DateTime? PackedAt,
    DateTime? CancelledAt);

// Filter shape for IPackTaskRepository.GetPagedAsync.
public sealed record PackTaskFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,                // matches PackNumber OR SoNumber
    string? Status = null,                // 'Pending' | 'Packed' | 'Cancelled'
    string SortBy = "generatedAt",
    bool SortDesc = true);

// Phase 15A — chip-count aggregate for /PackTasks list. 3-state
// (mirrors the 14D 3-state task machine).
public sealed record PackTaskStatusCounts(
    int All,
    int Pending,
    int Packed,
    int Cancelled);
