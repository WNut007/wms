using WMS.DAL.Repositories.Inventory;

namespace WMS.BLL.Services.Inventory;

// Phase 13 (ADR-012) — inter-warehouse Transfer workflow.
//
// State machine:
//   Draft → Submitted → Approved → InTransit → Received  (terminal happy)
//   pre-InTransit → Cancelled                            (terminal failure)
//   InTransit → Lost                                     (terminal failure)
public interface ITransferOrderService
{
    // CreateAsync — assigns TRN-YYYYMMDD-NNNN, persists header
    // (Status='Draft') + lines (line.Status='Pending'). Validates
    // From != To warehouse, owner consistency, non-empty lines.
    Task<TransferOrderDetail> CreateAsync(
        Guid tenantId,
        CreateTransferOrderRequest request,
        Guid currentUserId,
        CancellationToken ct = default);

    // SubmitAsync — Draft → Submitted. Idempotent on already-Submitted.
    Task<bool> SubmitAsync(
        Guid tenantId, Guid transferId, Guid currentUserId,
        CancellationToken ct = default);

    // ApproveAsync — Submitted → Approved. Separation of duties:
    // approver != requester. Idempotent on already-Approved.
    Task<bool> ApproveAsync(
        Guid tenantId, Guid transferId, Guid approverUserId,
        CancellationToken ct = default);

    // DispatchAsync — Approved → InTransit. TransactionScope-wrapped:
    //   For each line in Lines: validate QtyDispatched (≤ QtyRequested);
    //     subtract Stock at (FromLocation, Product, Lot, Pallet, Owner,
    //     Uom) via UpsertOnHand(-QtyDispatched, ctx) where ctx carries
    //     MovementType=Transfer + ReferenceType='TransferOrderLine'.
    //   Update line.QtyDispatched + line.Status='Dispatched'.
    //   Header transition Approved → InTransit.
    //
    // Lines NOT in the dispatch payload stay Pending (partial dispatch
    // allowed in v1 — the header still flips to InTransit when ANY
    // line is dispatched). Empty payload throws.
    //
    // CK_Stock_OnHand_NonNegative throws if source has insufficient
    // stock — TX rolls back.
    Task<bool> DispatchAsync(
        Guid tenantId,
        Guid transferId,
        IReadOnlyList<DispatchLineRequest> lines,
        Guid currentUserId,
        CancellationToken ct = default);

    // ReceiveAsync — InTransit → Received. TransactionScope-wrapped:
    //   For each line in Lines: validate QtyReceived (≤ QtyDispatched);
    //     add Stock at (ToLocation, ...) via UpsertOnHand(+QtyReceived,
    //     ctx). Update line.QtyReceived + line.Status='Received' or
    //     'Variance' (when QtyReceived < QtyDispatched).
    //   Header transition InTransit → Received.
    //
    // Lines not in payload stay Dispatched (their loss is recorded
    // in QtyLossInTransit when their default QtyReceived stays NULL —
    // operator can re-receive later). For v1 we require ALL Dispatched
    // lines to be in the receive payload.
    Task<bool> ReceiveAsync(
        Guid tenantId,
        Guid transferId,
        IReadOnlyList<ReceiveLineRequest> lines,
        Guid currentUserId,
        CancellationToken ct = default);

    // CancelAsync — pre-InTransit only (Draft / Submitted / Approved).
    // Requires non-blank reason. Idempotent on already-Cancelled.
    Task<bool> CancelAsync(
        Guid tenantId, Guid transferId, string reason,
        Guid currentUserId, CancellationToken ct = default);

    // MarkLostAsync — InTransit → Lost. No Stock writes (the loss
    // is captured by QtyLossInTransit on lines where QtyReceived
    // stays NULL forever). Requires non-blank reason. Idempotent.
    Task<bool> MarkLostAsync(
        Guid tenantId, Guid transferId, string reason,
        Guid currentUserId, CancellationToken ct = default);

    Task<TransferOrderDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<TransferOrderDetail?> GetByNumberAsync(
        Guid tenantId, string transferNumber, CancellationToken ct = default);
}
