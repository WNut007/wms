using WMS.DAL.Common;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Phase 11A (ADR-013) — tenant-scoped persistence for the Adjustment
// header. State transitions Pending → Applied / Rejected are exposed as
// dedicated atomic methods so the service can compose them inside a
// TransactionScope alongside Stock writes.
public interface IAdjustmentRepository
{
    // Inserts a Pending adjustment. Caller assigns Id + AdjustmentNumber.
    // RequestedBy is required (NOT NULL FK).
    Task CreateAsync(
        Adjustment entity, Guid requestedBy, CancellationToken ct = default);

    Task<Adjustment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Adjustment?> GetByNumberAsync(string adjustmentNumber, CancellationToken ct = default);

    // Phase 11A — list-page query. Same shape as PurchaseOrderRepository
    // .GetPagedAsync. JOINs Products + Warehouses + Locations + Owners
    // + UoMs + Users (requester) for resolved display fields.
    Task<PagedResult<AdjustmentListRow>> GetPagedAsync(
        AdjustmentFilter filter, CancellationToken ct = default);

    // Chip-count aggregate (mirrors Phase 10A pattern). Counts respect
    // search + warehouse + reason filter; ignore status.
    Task<AdjustmentStatusCounts> GetStatusCountsAsync(
        AdjustmentFilter filter, CancellationToken ct = default);

    // Phase 11A — atomic Apply transition. Sets Status='Applied',
    // ApprovedBy/At, AppliedAt, and stamps the resolved StockId.
    // Idempotent at SQL level (WHERE Status='Pending'). Returns true
    // when a row was changed.
    Task<bool> SetAppliedAsync(
        Guid adjustmentId,
        Guid stockId,
        Guid approvedBy,
        CancellationToken ct = default);

    // Phase 11A — atomic Reject transition. Sets Status='Rejected',
    // RejectedBy/At, RejectionReason. Idempotent at SQL level.
    Task<bool> SetRejectedAsync(
        Guid adjustmentId,
        string reason,
        Guid rejectedBy,
        CancellationToken ct = default);

    // Phase 11A — used by AdjustmentService to assign the next
    // AdjustmentNumber (ADJ-YYYYMMDD-NNNN). Counts existing rows for
    // today's date prefix.
    Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default);
}
