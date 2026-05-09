using WMS.DAL.Repositories.Inbound;

namespace WMS.BLL.Services.Inbound;

// Inbound order-side primitives. CreateAsync persists a fresh PO
// header + lines transactionally and returns the resulting Detail
// (header + lines re-fetched, so callers see DB-defaulted CreatedAt
// / Status / Version). Get methods return null when not found.
//
// Phase 9A added: UpdateAsync (Edit form), ArchiveAsync (cancel),
// MarkReceivingAsync + MarkClosedAsync (idempotent status transitions
// called by the GR flow once 9B ships).
public interface IPurchaseOrderService
{
    Task<PurchaseOrderDetail> CreateAsync(
        Guid tenantId,
        CreatePurchaseOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);

    Task<PurchaseOrderDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<PurchaseOrderDetail?> GetByNumberAsync(
        Guid tenantId, string poNumber, CancellationToken ct = default);

    // Phase 9A — Edit form. Updates header (ExpectedDate + Notes
    // editable; PoNumber/OwnerId/WarehouseId frozen post-create) and
    // optionally replaces all lines. Lines update is rejected with
    // InvalidOperationException when any line on the PO has
    // ReceivedQuantity > 0 (= a receipt has bumped it; cancel-and-
    // recreate is the upgrade path until receipt-aware line edit
    // ships in Phase 10).
    Task<PurchaseOrderDetail> UpdateAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        UpdatePurchaseOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);

    // Phase 9A — Cancel a PO. Sets header Status='Cancelled' (only
    // valid from Open or Receiving); cascades Cancelled to all lines
    // currently in Open or PartiallyReceived (lines with stock still
    // sit at PartiallyReceived but the header is final).
    // Idempotent — already-Cancelled returns false.
    Task<bool> ArchiveAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid? currentUserId,
        CancellationToken ct = default);

    // Phase 9A — called by GR flow when the first receipt against a
    // PO posts. Atomic transition Open → Receiving; idempotent (returns
    // false if already in Receiving / Closed / Cancelled).
    Task<bool> MarkReceivingAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid? currentUserId,
        CancellationToken ct = default);

    // Phase 9A — called by GR flow after every receipt. Checks
    // AllLinesFullyReceived; if true, atomically transitions
    // Receiving → Closed. Idempotent — returns false on no-op.
    Task<bool> MarkClosedAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid? currentUserId,
        CancellationToken ct = default);

    // Phase 10B (TD-023) — called by GR cancellation flow after the
    // per-line ReceivedQuantity decrements have landed. Computes the
    // appropriate target header status from the post-decrement state:
    //   AllLinesFullyReceived              → stay Closed (no-op)
    //   No line has any receipts           → Open
    //   Otherwise (some receipts remain)   → Receiving
    // Idempotent — returns false on no-op (e.g. cancellation that
    // didn't change the aggregate enough to flip the header). Will
    // not touch a Cancelled PO (separate, user-driven state).
    Task<bool> RevertStatusAfterCancelAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid? currentUserId,
        CancellationToken ct = default);
}
