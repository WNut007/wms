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
}

// Phase 14E — return shape for GenerateAsync. Carries enough for the
// controller to redirect to /Shipments/Detail/{ShipmentNumber} and
// surface a summary banner without re-querying.
public sealed record ShipmentGenerationResult(
    Guid ShipmentId,
    string ShipmentNumber);
