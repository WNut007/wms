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
}

// Phase 14C — return shape for GenerateAsync. Carries enough for the
// controller to redirect to /PickTasks/Detail/{PickNumber} and surface
// a summary banner without re-querying.
public sealed record PickTaskGenerationResult(
    Guid PickTaskId,
    string PickNumber,
    int LineCount,
    decimal TotalExpectedQuantity);
