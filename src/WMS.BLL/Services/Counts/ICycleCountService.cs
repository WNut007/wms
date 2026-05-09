using WMS.DAL.Repositories.Counts;
using WMS.Domain.Entities.Counts;

namespace WMS.BLL.Services.Counts;

// Phase 12 — cycle count workflow service.
// State machine: Counting → Review → Applied; Cancelled is a terminal
// state from any non-Applied state. Apply atomically transitions
// Review → Applied AND writes per-line Stock + Movement Log entries
// (MovementType=Cycle) inside one TransactionScope.
public interface ICycleCountService
{
    // CreateAsync — snapshots positive-OnHand stock at the warehouse
    // (filtered to LocationFilter when set). Assigns a tenant-wide
    // unique CountNumber (CYC-YYYYMMDD-NNNN). Lines persist with
    // CountedQuantity=NULL + LineStatus='Pending'. Empty snapshot
    // (no positive-OnHand stock at scope) throws InvalidOperationException
    // — no point counting nothing.
    Task<CycleCountDetail> CreateAsync(
        Guid tenantId,
        CreateCycleCountRequest request,
        Guid currentUserId,
        CancellationToken ct = default);

    // SaveCountedQuantitiesAsync — bulk update of CountedQuantity +
    // LineStatus + Notes on lines belonging to the session. Allowed
    // only when session is in Counting state. Allows partial saves
    // (operator can save progress and resume later). Atomic per call.
    Task SaveCountedQuantitiesAsync(
        Guid tenantId,
        Guid cycleCountId,
        IReadOnlyList<CountLineUpdate> updates,
        Guid currentUserId,
        CancellationToken ct = default);

    // SubmitForReviewAsync — Counting → Review. Records CountedBy/At
    // on the header. Idempotent on already-Review (returns false).
    Task<bool> SubmitForReviewAsync(
        Guid tenantId,
        Guid cycleCountId,
        Guid currentUserId,
        CancellationToken ct = default);

    // ApproveAndApplyAsync — Review → Applied + per-line Stock +
    // Movement Log writes inside one TransactionScope.
    //
    // Per-line behavior:
    //   * LineStatus = 'Pending' / 'Skipped' → ignored on apply.
    //   * LineStatus = 'Counted' AND Variance != 0 → write
    //     Stock UpsertOnHand(6-tuple key, variance, ctx) where ctx
    //     carries MovementType=Cycle + ReferenceType='CycleCountLine'
    //     + ReferenceId=line.Id + Notes carrying the count number.
    //   * LineStatus = 'Counted' AND Variance == 0 → no Stock write,
    //     no Movement Log row (verified-as-correct).
    //
    // Throws when:
    //   * cycleCountId doesn't exist
    //   * Status != Review (Counting can't apply directly; Cancelled /
    //     Applied are terminal)
    //   * approverUserId == header.CountedBy (separation of duties —
    //     a counter cannot approve their own count)
    //   * any Stock UpsertOnHand throws (e.g. CK_Stock_OnHand_NonNegative
    //     when a counted-down delta would underflow). TX rolls back —
    //     no partial application.
    //
    // Returns true on successful state change; false on already-
    // Applied (idempotent).
    Task<bool> ApproveAndApplyAsync(
        Guid tenantId,
        Guid cycleCountId,
        Guid approverUserId,
        CancellationToken ct = default);

    // CancelAsync — Counting OR Review → Cancelled. Requires non-blank
    // reason (audit). Idempotent on already-Cancelled. Cannot cancel
    // an Applied session.
    Task<bool> CancelAsync(
        Guid tenantId,
        Guid cycleCountId,
        string reason,
        Guid currentUserId,
        CancellationToken ct = default);

    Task<CycleCountDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<CycleCountDetail?> GetByNumberAsync(
        Guid tenantId, string countNumber, CancellationToken ct = default);
}
