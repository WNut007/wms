using WMS.DAL.Repositories.Inbound;

namespace WMS.BLL.Services.Inbound;

// Operational receiving primitive. PostReceivingAsync persists the
// receiving header + lines, drives stock + lot + pallet upserts via
// IReceivingService (β) for each line, and bumps PurchaseOrderLine
// ReceivedQuantity for any line that names a PO line.
//
// Status orchestration (PO header / line / receiving Draft→Posted) is
// out of scope for this chunk; the receiving header is created at
// 'Posted' and the PO header / line keep their existing Status until
// a future chunk owns the transition rules.
public interface IReceivingHeaderService
{
    Task<ReceivingDetail> PostReceivingAsync(
        Guid tenantId,
        PostReceivingRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);

    Task<ReceivingDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<ReceivingDetail?> GetByNumberAsync(
        Guid tenantId, string receivingNumber, CancellationToken ct = default);

    // Phase 10B (TD-023) — reverses a posted receipt. Wraps the full
    // multi-write flow in a TransactionScope:
    //   1. For each line: subtract ReceivedQuantity from the matching
    //      Stock row at the 6-tuple key, writing a paired negative
    //      StockMovement (MovementType=Adjust, ReferenceType=
    //      'ReceivingLineCancellation') in the same SQL batch.
    //   2. Decrement linked PO line ReceivedQuantity for every line
    //      that names a PurchaseOrderLineId.
    //   3. Flip ReceivingHeader.Status='Cancelled', stamp Cancelled
    //      audit trio (CancelledBy / CancelledAt / CancelReason).
    //   4. Revert PO line statuses (Closed → PartiallyReceived /
    //      Open, etc.) per the new ReceivedQty.
    //   5. Revert PO header status if needed (Closed → Receiving /
    //      Open) via IPurchaseOrderService.RevertStatusAfterCancelAsync.
    //
    // Returns true on a successful state change; false when the
    // receipt is already Cancelled (idempotent) or the row is
    // missing. Throws InvalidOperationException for invalid source
    // states (Draft must be discarded via the future DiscardDraft
    // path; this method targets Posted only) or when underflowing
    // a Stock row's CK_Stock_OnHand_NonNegative — the operator
    // already consumed the received stock and must adjust manually.
    Task<bool> CancelReceivingAsync(
        Guid tenantId,
        Guid receivingHeaderId,
        string reason,
        Guid? currentUserId,
        CancellationToken ct = default);
}
