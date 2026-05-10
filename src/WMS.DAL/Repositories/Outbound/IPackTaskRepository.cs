using WMS.DAL.Common;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14D — tenant-scoped persistence for outbound.PackTasks +
// PackTaskLines + outbound.Cartons. Same ambient-TX-aware write
// pattern as Phase 14C PickTaskRepository.
public interface IPackTaskRepository
{
    // Atomic header + lines insert. Caller pre-assigns every Id +
    // PackNumber. Header lands as 'Pending' / line LineStatus default
    // 'Pending' via DB defaults. GeneratedAt populates server-side.
    // No Carton on Create — that's a SubmitAsync concern.
    Task CreateAsync(
        PackTask header,
        IReadOnlyList<PackTaskLine> lines,
        Guid? userId,
        CancellationToken ct = default);

    // Header + lines + carton in one round-trip via QueryMultiple.
    // Null when the task doesn't exist.
    Task<PackTaskDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Same shape as GetByIdAsync, resolved by tenant-wide-unique
    // PackNumber.
    Task<PackTaskDetail?> GetByNumberAsync(
        string packNumber, CancellationToken ct = default);

    // Phase 14D — pre-generation guard. One Active (Pending) pack task
    // per SO. Returns null when no active task exists.
    Task<PackTask?> GetActiveBySalesOrderAsync(
        Guid salesOrderId, CancellationToken ct = default);

    // ----- State transitions (header) -----
    // All idempotent at SQL level via WHERE Status=@from filter.

    // Pending → Packed. Stamps PackedAt/By.
    Task<bool> SetPackedAsync(
        Guid packTaskId, Guid? userId, CancellationToken ct = default);

    // Pending → Cancelled. Required reason.
    Task<bool> SetCancelledAsync(
        Guid packTaskId, string reason, Guid? userId,
        CancellationToken ct = default);

    // ----- Per-line updates -----

    // Submit-time per-line update: sets PackedQuantity + LineStatus +
    // ShortPackReason + Notes. CK_PackTaskLines_StatusMatchesQty
    // enforces the (status,qty) invariant; CK_*_PackedNotOverPicked
    // enforces qty ceiling. Caller composes inside ambient TX.
    Task UpdateLinePackedAsync(
        Guid lineId,
        decimal? packedQuantity,
        string lineStatus,
        string? shortPackReason,
        string? notes,
        Guid? userId,
        CancellationToken ct = default);

    // ----- Number assignment -----

    // PACK-YYYYMMDD-NNNN — count of tasks with PackNumber LIKE prefix.
    Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default);

    // ----- Phase 15A — list-page reads -----

    Task<PagedResult<PackTaskListRow>> GetPagedAsync(
        PackTaskFilter filter, CancellationToken ct = default);

    Task<PackTaskStatusCounts> GetStatusCountsAsync(
        PackTaskFilter filter, CancellationToken ct = default);
}

public interface IPackTaskRepositoryFactory
{
    IPackTaskRepository For(Guid tenantId);
}
