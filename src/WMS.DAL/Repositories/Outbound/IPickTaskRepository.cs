using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14C — tenant-scoped persistence for outbound.PickTasks +
// PickTaskLines. State transitions are atomic single-statement
// UPDATEs so PickTaskService can compose them inside a Transaction-
// Scope alongside Stock writes + OrderAllocation flips + line-qty
// bumps (Phase 11A/12/13/14B established pattern).
public interface IPickTaskRepository
{
    // Atomic header + lines insert. Caller pre-assigns every Id +
    // PickNumber. Lines snapshot the SO's Active OrderAllocations at
    // generation time (one PickTaskLine per OrderAllocation row).
    // Header lands as 'Pending' / line LineStatus default 'Pending'
    // via DB defaults. GeneratedAt populates server-side.
    Task CreateAsync(
        PickTask header,
        IReadOnlyList<PickTaskLine> lines,
        Guid? userId,
        CancellationToken ct = default);

    // Header + lines in one round-trip via QueryMultiple. Null when
    // the task doesn't exist.
    Task<PickTaskDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Same shape as GetByIdAsync, resolved by tenant-wide-unique
    // PickNumber.
    Task<PickTaskDetail?> GetByNumberAsync(
        string pickNumber, CancellationToken ct = default);

    // Phase 14C — pre-generation guard. One Active (Pending or
    // InProgress) pick task per SO. Returns null when no active task
    // exists (caller may proceed to GenerateAsync).
    Task<PickTask?> GetActiveBySalesOrderAsync(
        Guid salesOrderId, CancellationToken ct = default);

    // ----- State transitions (header) -----
    // All idempotent at SQL level via WHERE Status=@from filter.

    // Pending → InProgress. Stamps StartedAt/StartedBy.
    Task<bool> SetStartedAsync(
        Guid pickTaskId, Guid? userId, CancellationToken ct = default);

    // InProgress → Picked | PartiallyPicked. Stamps CompletedAt/By.
    // Caller picks the target status from the per-line aggregate
    // (any line short → PartiallyPicked, otherwise Picked).
    Task<bool> SetCompletedAsync(
        Guid pickTaskId,
        string targetStatus,
        Guid? userId,
        CancellationToken ct = default);

    // Pending|InProgress → Cancelled. Required reason. Caller passes
    // the from state for atomicity (idempotent on already-Cancelled).
    Task<bool> SetCancelledAsync(
        Guid pickTaskId,
        string fromStatus,
        string reason,
        Guid? userId,
        CancellationToken ct = default);

    // ----- Per-line updates -----

    // Submit-time per-line update: sets PickedQuantity + LineStatus +
    // ShortPickReason + Notes. CK_PickTaskLines_StatusMatchesQty
    // enforces the (status,qty) invariant; CK_*_PickedNotOverExpected
    // enforces qty ceiling. Caller composes inside ambient TX.
    Task UpdateLinePickedAsync(
        Guid lineId,
        decimal? pickedQuantity,
        string lineStatus,
        string? shortPickReason,
        string? notes,
        Guid? userId,
        CancellationToken ct = default);

    // ----- Number assignment -----

    // PICK-YYYYMMDD-NNNN — count of tasks with PickNumber LIKE prefix.
    Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default);
}

public interface IPickTaskRepositoryFactory
{
    IPickTaskRepository For(Guid tenantId);
}
