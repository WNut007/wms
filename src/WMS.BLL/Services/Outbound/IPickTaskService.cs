namespace WMS.BLL.Services.Outbound;

// Phase 14C — pick task lifecycle orchestration. One service spans
// generation (T4 — this method) → execution (T5 SubmitAsync) → cancel-
// reversal (T6 CancelAsync).
//
// State machine handled here:
//   SO Allocated   → Picking         (GenerateAsync — task created)
//   SO Picking     → Picked          (SubmitAsync — full pick, T5)
//   SO Picking     → PartiallyPicked (SubmitAsync — short pick, T5)
//   SO Picking     → Allocated       (CancelAsync — reversal, T6)
//
// PickTask state machine:
//   Pending    → InProgress (operator entered first quantity)    — T5/T7
//   InProgress → Picked | PartiallyPicked (submitted)            — T5
//   Pending|InProgress → Cancelled (reversal restores allocation) — T6
public interface IPickTaskService
{
    // Generates a new pick task from the SO's Active OrderAllocations.
    // Per allocation = one PickTaskLine snapshotting the Stock 6-tuple
    // at generation time (stable display + reporting; matches Phase 12
    // cycle-count line snapshot pattern).
    //
    // Pre-conditions:
    //   - SO exists
    //   - SO.Status = 'Allocated' (Picking returns existing task — idempotent)
    //   - No Active (Pending|InProgress) pick task for the SO
    //   - At least one Active OrderAllocation on the SO
    //
    // Side effects (one TransactionScope):
    //   - INSERT outbound.PickTasks (Pending)
    //   - INSERT outbound.PickTaskLines per allocation
    //   - UPDATE outbound.SalesOrders SET Status='Picking'
    //
    // Stock + OrderAllocation rows are NOT mutated here — those flips
    // belong to SubmitAsync (T5) when the operator commits the actual
    // pick quantities.
    Task<PickTaskGenerationResult> GenerateAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid currentUserId,
        CancellationToken ct = default);

    // Phase 14C — final commit. Operator-supplied per-line picked
    // quantities flow through here in one TransactionScope:
    //   - PickTaskLines: PickedQuantity + LineStatus + ShortPickReason
    //     + Notes (per-line UPDATE)
    //   - Stock: OnHand decrement (per Pick movement, only when picked
    //     qty > 0); QuantityAllocated decrement by the full Expected
    //     (the entire reservation is consumed — picked portion went
    //     out via OnHand, unfilled portion is freed back to available)
    //   - SalesOrderLines: PickedQuantity bump (+ picked qty);
    //     AllocatedQuantity decrement (- Expected qty)
    //   - OrderAllocations: Active → Picked (audit stamped)
    //   - PickTask: Pending|InProgress → Picked | PartiallyPicked
    //   - SalesOrders: Picking → Picked (every line picked at full
    //     OrderedQty) | PartiallyPicked (any line short or skipped)
    //
    // Validation:
    //   - Task in Pending or InProgress state
    //   - Every task line present in request, no extras, no dups
    //   - LineStatus ∈ {'Picked','Skipped'}
    //   - Picked: PickedQuantity in 0..Expected; ShortPickReason
    //     required when PickedQty < Expected
    //   - Skipped: PickedQuantity must be null; ShortPickReason
    //     required (Skipped IS a short — full)
    //
    // Throws InvalidOperationException on state / shape violations.
    // CK_Stock_OnHand_NonNegative throws if a parallel pick already
    // drained the stock — TX rolls back, caller surfaces the error.
    Task<PickTaskSubmissionResult> SubmitAsync(
        Guid tenantId,
        SubmitPickTaskRequest request,
        Guid currentUserId,
        CancellationToken ct = default);

    // Phase 14C — pre-Submit reversal. Pending or InProgress task →
    // Cancelled with required reason. SO Picking → Allocated (the
    // SO's allocations were never touched by Generate; they're still
    // Active and the SO returns to its pre-pick state).
    //
    // No Stock writes, no allocation flips — Generate didn't mutate
    // either, and Submit is the only operation that does. Per-line
    // PickedQuantity / LineStatus values entered via future "Save
    // Progress" stay frozen on the cancelled task as audit history;
    // they're no longer relevant to execution.
    //
    // Idempotent on already-Cancelled (returns false). Rejects
    // Picked / PartiallyPicked tasks — post-Submit reversal needs a
    // separate "return to stock" workflow (future phase).
    //
    // Returns true when the cancel actually flipped the task; false
    // for the no-op idempotent re-trigger.
    Task<bool> CancelAsync(
        Guid tenantId,
        Guid pickTaskId,
        string reason,
        Guid currentUserId,
        CancellationToken ct = default);
}

// Phase 14C — return shape for GenerateAsync. Carries enough for the
// controller to redirect to /PickTasks/Detail/{PickNumber} and surface
// a summary banner without re-querying.
public sealed record PickTaskGenerationResult(
    Guid PickTaskId,
    string PickNumber,
    int LineCount,
    decimal TotalExpectedQuantity);

// Phase 14C — input shape for SubmitAsync. One PickedLineEntry per
// PickTaskLine in the task; the service rejects missing or extra
// LineIds.
public sealed record SubmitPickTaskRequest(
    Guid PickTaskId,
    IReadOnlyList<PickedLineEntry> Lines);

public sealed record PickedLineEntry(
    Guid LineId,
    decimal? PickedQuantity,
    string LineStatus,         // 'Picked' | 'Skipped'
    string? ShortPickReason,
    string? Notes);

// Phase 14C — return shape for SubmitAsync. Surfaces the resolved
// terminal task + SO statuses + per-line outcome counts so the
// controller can render a one-line "{n} short, {m} skipped" banner
// without re-querying.
public sealed record PickTaskSubmissionResult(
    string TaskStatus,             // 'Picked' | 'PartiallyPicked'
    string SalesOrderStatus,       // 'Picked' | 'PartiallyPicked'
    int FullyPickedLineCount,
    int ShortPickedLineCount,
    int SkippedLineCount,
    decimal TotalPickedQuantity);
