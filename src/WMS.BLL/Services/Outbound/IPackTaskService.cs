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

    // Phase 14D — final commit. Operator-supplied per-line packed
    // quantities + carton metadata flow through here in one
    // TransactionScope:
    //   - PackTaskLines: PackedQuantity + LineStatus + ShortPackReason
    //     + Notes (per-line UPDATE)
    //   - Cartons: INSERT (one per task for MVP; CartonNumber stamped
    //     server-side as CTN-YYYYMMDD-NNNN)
    //   - PackTask: Pending → Packed (audit stamped)
    //   - SalesOrders: Picked → Packed | PartiallyPicked → Packed
    //
    // No Stock writes — pack is post-stock; the qty already left
    // inventory at pick submit (Phase 14C). PackedQty < PickedQty
    // surfaces as a discrepancy on the pack task lines but the SO
    // still flips to Packed (the carton is sealed; downstream
    // reconciliation needs a separate return-to-stock workflow).
    //
    // Validation:
    //   - Task in Pending state
    //   - Every task line present in request, no extras, no dups
    //   - LineStatus ∈ {'Packed', 'Skipped'}
    //   - Packed: PackedQuantity in 0..PickedQuantity; ShortPackReason
    //     required when PackedQty < PickedQty
    //   - Skipped: PackedQuantity must be null; ShortPackReason
    //     required (Skipped IS a short — full)
    //   - Carton.WeightKg if supplied must be non-negative
    //
    // Throws InvalidOperationException on state / shape violations.
    Task<PackTaskSubmissionResult> SubmitAsync(
        Guid tenantId,
        SubmitPackTaskRequest request,
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

// Phase 14D — input shape for SubmitAsync. One PackedLineEntry per
// PackTaskLine in the task; the service rejects missing or extra
// LineIds. Plus the single Carton's metadata (1:1 with task for MVP).
public sealed record SubmitPackTaskRequest(
    Guid PackTaskId,
    IReadOnlyList<PackedLineEntry> Lines,
    Guid? BoxTypeId,
    decimal? WeightKg,
    string? CartonNotes);

public sealed record PackedLineEntry(
    Guid LineId,
    decimal? PackedQuantity,
    string LineStatus,         // 'Packed' | 'Skipped'
    string? ShortPackReason,
    string? Notes);

// Phase 14D — return shape for SubmitAsync.
public sealed record PackTaskSubmissionResult(
    string TaskStatus,             // always 'Packed' on success
    string SalesOrderStatus,       // 'Packed'
    int FullyPackedLineCount,
    int ShortPackedLineCount,
    int SkippedLineCount,
    decimal TotalPackedQuantity,
    string CartonNumber);
