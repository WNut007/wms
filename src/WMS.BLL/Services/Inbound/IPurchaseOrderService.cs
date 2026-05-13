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

    // Block 4.5.2 d.2.3.a (TD-026 closure prep) — surgical partial
    // update. Replaces the wholesale UpdateAsync flow for Edit POST so
    // per-line locked state can be preserved.
    //
    // Each operation classified against authoritative DB state:
    //   LineUpdates  → refuse when target ReceivedQuantity > 0 (locked).
    //                  UPDATE never touches ReceivedQuantity / LineNumber
    //                  / LineNo / DisplayOrder / Status.
    //   LineInserts  → refuse on LineNumber collision with existing or
    //                  among inserts. ReceivedQuantity hardcoded 0 in
    //                  INSERT (operator-supplied value not parameterised).
    //   LineDeletes  → refuse when target ReceivedQuantity > 0 (locked).
    //                  FK NO ACTION on ReceivingLines is the in-flight-
    //                  race last-resort guard.
    //
    // All operations + header update wrapped in a single TransactionScope
    // (TD-022 multi-connection MSDTC trade-off accepted per Phase 10B /
    // 11A / 12 precedent). Mixed-batch atomicity: any failure rolls back
    // every write, including header.
    //
    // Closed/Cancelled POs refused outright (controller redirects first;
    // defence in depth here).
    Task<PurchaseOrderDetail> UpdatePartialAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        PartialUpdatePurchaseOrderRequest request,
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
