namespace WMS.BLL.Services.Outbound;

// Phase 14D — pack task lifecycle orchestration. T4 ships GenerateAsync;
// T5 adds SubmitAsync (TX-wrapped — flips SO Picked|PartiallyPicked →
// Packed); T6 adds CancelAsync (pre-Submit reversal).
//
// State machine handled here:
//   PackTask Pending → Packed | Cancelled
//   SO       Picked|PartiallyPicked → Packed (on SubmitAsync)
//
// Note: SO header does NOT flip on Generate (no 'Packing' intermediate
// state for MVP — pack workflow is single-shot). Per-SO pack-in-flight
// is detected via GetActiveBySalesOrderAsync.
public interface IPackTaskService
{
    // Generates a new pack task from the SO's positively-picked lines.
    // Per SO line with PickedQuantity > 0 = one PackTaskLine (Skipped
    // pick lines + zero-pick lines do NOT spawn pack lines).
    //
    // Pre-conditions:
    //   - SO exists
    //   - SO.Status ∈ {'Picked', 'PartiallyPicked'} (Packed returns
    //     the existing packed task — but the typical idempotent path
    //     is "Pending pack task already exists" → return it; Cancelled
    //     and pre-pick states throw)
    //   - No Active (Pending) pack task for the SO — if one exists,
    //     return it idempotently (controller redirects to its Detail)
    //   - At least one SO line with PickedQuantity > 0
    //
    // Side effects (no TX needed — single repo write):
    //   - INSERT outbound.PackTasks (Pending)
    //   - INSERT outbound.PackTaskLines per positively-picked SO line
    //
    // SO header status is NOT mutated — operator sees SO stays Picked|
    // PartiallyPicked while pack is in flight; flips to Packed only on
    // SubmitAsync.
    Task<PackTaskGenerationResult> GenerateAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid currentUserId,
        CancellationToken ct = default);
}

// Phase 14D — return shape for GenerateAsync. Carries enough for the
// controller to redirect to /PackTasks/Detail/{PackNumber} and surface
// a summary banner without re-querying.
public sealed record PackTaskGenerationResult(
    Guid PackTaskId,
    string PackNumber,
    int LineCount,
    decimal TotalPickedQuantity);
