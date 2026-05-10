namespace WMS.BLL.Services.Outbound;

// Phase 14E — shipment lifecycle orchestration. T4 ships GenerateAsync;
// T5 adds SubmitAsync (TX-wrapped — flips SO Packed → Shipped + stamps
// cartons); T6 adds CancelAsync (pre-Submit reversal).
//
// State machine handled here:
//   Shipment Pending → Shipped | Cancelled
//   SO       Packed → Shipped (on SubmitAsync)
//
// Note: SO header does NOT flip on Generate (no 'Shipping' intermediate
// state for MVP — ship workflow is single-shot, mirrors 14D Pack).
// Per-SO ship-in-flight is detected via GetActiveBySalesOrderAsync.
public interface IShipmentService
{
    // Generates a new shipment record for a Packed SO.
    //
    // Pre-conditions:
    //   - SO exists
    //   - SO.Status = 'Packed'
    //   - No Active (Pending) shipment for the SO (idempotent — if
    //     one exists, return it; controller redirects to its Detail)
    //
    // Side effects (no TX needed — single repo INSERT):
    //   - INSERT outbound.Shipments (Pending, no carrier/tracking yet)
    //
    // SO header status is NOT mutated — operator sees SO stays Packed
    // while ship is in flight; flips to Shipped only on SubmitAsync.
    Task<ShipmentGenerationResult> GenerateAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid currentUserId,
        CancellationToken ct = default);

    // Phase 14E — final commit. Operator-supplied dispatch metadata
    // (CarrierName, TrackingNumber, Notes) flows through here in one
    // TransactionScope:
    //   - Shipment: Pending → Shipped + stamp dispatch metadata + audit
    //   - Cartons: bulk UPDATE SET ShipmentId for every carton
    //     belonging to the SO (resolved via PackTask.SalesOrderId)
    //   - SalesOrders: Packed → Shipped
    //
    // No Stock writes — ship is post-stock; the qty already left
    // inventory at pick submit (Phase 14C). Pack recorded the package
    // (Phase 14D); ship records the dispatch.
    //
    // Validation:
    //   - Shipment in Pending state
    //   - CarrierName + TrackingNumber + Notes are all optional (the
    //     deferred-default carrier pattern — operator may not have
    //     them at ship time)
    //   - CarrierName trimmed to ≤50 chars / TrackingNumber to ≤100
    //     (matches column widths)
    //
    // Throws InvalidOperationException on state violations.
    Task<ShipmentSubmissionResult> SubmitAsync(
        Guid tenantId,
        SubmitShipmentRequest request,
        Guid currentUserId,
        CancellationToken ct = default);

    // Phase 14E — pre-Submit reversal. Pending shipment → Cancelled
    // with required reason. SO state is NOT touched (Generate didn't
    // flip it — SO stayed Packed; cancelling the shipment leaves the
    // SO in Packed, ready for re-Generate by another operator).
    //
    // No carton stamping reversal (Pending shipments haven't claimed
    // any cartons — that's a SubmitAsync concern). No TX needed —
    // single repo write.
    //
    // Idempotent on already-Cancelled (returns false). Rejects Shipped
    // (post-Submit terminal — reversing a posted shipment needs a
    // separate workflow, future TD).
    Task<bool> CancelAsync(
        Guid tenantId,
        Guid shipmentId,
        string reason,
        Guid currentUserId,
        CancellationToken ct = default);
}

// Phase 14E — return shape for GenerateAsync. Carries enough for the
// controller to redirect to /Shipments/Detail/{ShipmentNumber} and
// surface a summary banner without re-querying.
public sealed record ShipmentGenerationResult(
    Guid ShipmentId,
    string ShipmentNumber);

// Phase 14E — input shape for SubmitAsync. Single-shipment per SO for
// MVP, so no per-line breakdown.
public sealed record SubmitShipmentRequest(
    Guid ShipmentId,
    string? CarrierName,
    string? TrackingNumber,
    string? Notes);

// Phase 14E — return shape for SubmitAsync.
public sealed record ShipmentSubmissionResult(
    string ShipmentStatus,         // always 'Shipped' on success
    string SalesOrderStatus,       // 'Shipped'
    int CartonCount);              // cartons stamped with this ShipmentId
