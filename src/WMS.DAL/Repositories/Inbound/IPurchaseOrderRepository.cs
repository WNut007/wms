using WMS.DAL.Common;
using WMS.Domain.Entities.Inbound;

namespace WMS.DAL.Repositories.Inbound;

// Tenant-scoped CRUD-ish primitives for inbound.PurchaseOrders +
// PurchaseOrderLines. The repo handles the multi-table shape (one
// header, many lines) so the service doesn't have to coordinate
// transactions itself.
public interface IPurchaseOrderRepository
{
    // Inserts the header + lines in a single transaction. Both inputs'
    // Id properties must be pre-set by the caller (typically via
    // Guid.NewGuid()); the repo writes them as-is so the returned
    // header.Id matches what the caller already holds.
    //
    // Audit fields (CreatedAt default + CreatedBy = userId) are stamped
    // by the repo on every row.
    Task CreateAsync(
        PurchaseOrder header,
        IReadOnlyList<PurchaseOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default);

    // Header + lines in one round-trip via QueryMultiple — null when
    // the PO doesn't exist.
    Task<PurchaseOrderDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Same shape as GetByIdAsync but resolves by tenant-wide-unique
    // PoNumber. Convenience for scan-driven lookups (operator types
    // a PO number from a delivery note).
    Task<PurchaseOrderDetail?> GetByNumberAsync(string poNumber, CancellationToken ct = default);

    // Atomic ReceivedQuantity bump on a single line. Called per
    // receiving line that's linked to a PO line. The CHECK
    // CK_PurchaseOrderLines_ReceivedQty_NonNegative enforces the
    // invariant; the service is expected to pass a positive delta.
    // No version check today — receipts are append-only and serialise
    // naturally on the row's UPDATE lock.
    Task IncrementLineReceivedQuantityAsync(
        Guid poLineId,
        decimal delta,
        Guid? userId,
        CancellationToken ct = default);

    // Phase 9A — list-page query. JOINs Owners (Code+Name) + Warehouses
    // (Code) + per-PO aggregate from PurchaseOrderLines (LineCount,
    // TotalExpectedQty, TotalReceivedQty). Returns paged rows + total
    // count in one QueryMultiple round-trip. SortBy whitelisted via
    // PurchaseOrderSortMapper.
    Task<PagedResult<PurchaseOrderListRow>> GetPagedAsync(
        PurchaseOrderFilter filter, CancellationToken ct = default);

    // Phase 9A — Edit form support: header fields update only.
    // PoNumber, OwnerId, WarehouseId NOT in SET — Code-shaped natural
    // keys are read-only (FK-orphan risk on receipts that point at
    // (PoId, OwnerId)); ExpectedDate + Notes editable. Returns
    // rows-affected > 0.
    Task<bool> UpdateHeaderAsync(
        PurchaseOrder entity, Guid? userId, CancellationToken ct = default);

    // Phase 9A — Edit form support: replace all lines on a PO. Caller
    // is expected to have verified zero receipts exist (lines locked
    // when any line has ReceivedQuantity > 0). Atomic: DELETE old +
    // INSERT new in one transaction.
    Task ReplaceLinesAsync(
        Guid purchaseOrderId,
        IReadOnlyList<PurchaseOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default);

    // Phase 9A — atomic status setter. fromStatus filter makes
    // transitions idempotent (UPDATE WHERE Status=@from returns 0
    // rows when already in target state). Returns true when a row
    // was changed; false on no-op or missing PO.
    Task<bool> SetStatusAsync(
        Guid purchaseOrderId,
        string fromStatus,
        string toStatus,
        Guid? userId,
        CancellationToken ct = default);

    // Phase 9A — Edit form lock check. Returns count of lines on this
    // PO with ReceivedQuantity > 0. Edit form uses this to decide
    // whether the Lines section is editable: zero = full edit allowed;
    // any > 0 = lock lines, header-only edit (cancel-and-recreate is
    // the upgrade path until receipt-aware line edit lands in Phase 10).
    Task<int> CountReceivedLinesAsync(
        Guid purchaseOrderId, CancellationToken ct = default);

    // Phase 9A — service-level "all lines closed" predicate for
    // MarkClosedAsync. True iff every line on this PO has
    // ReceivedQuantity >= ExpectedQuantity (= line is full). Atomic
    // single-query — used by GR service after a receipt to decide
    // whether the PO transitions Receiving → Closed.
    Task<bool> AllLinesFullyReceivedAsync(
        Guid purchaseOrderId, CancellationToken ct = default);

    // Phase 9A — bulk-cancel all open lines. Used by ArchiveAsync
    // when the PO is cancelled — propagates Cancelled to children.
    // Lines with ReceivedQuantity > 0 are left at their current
    // status (PartiallyReceived or Closed); only Open / Partially-
    // Received lines flip to Cancelled.
    Task CancelOpenLinesAsync(
        Guid purchaseOrderId, Guid? userId, CancellationToken ct = default);

    // Phase 10A (TD-029) — resolves product code+name + UoM code via
    // JOIN for the PO Detail Lines tab. Replaces the raw entity list
    // (which carries Guid-only FKs) for any read surface that needs to
    // display human-readable identifiers.
    Task<IReadOnlyList<PurchaseOrderLineRow>> GetLineRowsByIdAsync(
        Guid purchaseOrderId, CancellationToken ct = default);

    // Phase 10A (TD-028) — chip-count aggregates for the list page.
    // Returns one row of per-status counts that respects the search
    // filter but ignores the status filter (so all chips can render
    // their counts in one round-trip alongside the rows + total).
    Task<PurchaseOrderStatusCounts> GetStatusCountsAsync(
        PurchaseOrderFilter filter, CancellationToken ct = default);

    // Phase 10B (TD-023) — predicate for PO header revert. True when
    // any non-Cancelled line still has ReceivedQty > 0 (= the PO has
    // outstanding receipts beyond the one being cancelled).
    Task<bool> AnyLineHasReceiptsAsync(
        Guid purchaseOrderId, CancellationToken ct = default);

    // Phase 10B (TD-023) — atomic per-line Status revert after a
    // receipt cancellation drops ReceivedQuantity. Computes the
    // target status server-side from current Received vs Expected:
    //   ReceivedQty == 0                → Open
    //   ReceivedQty <  ExpectedQty      → PartiallyReceived
    //   ReceivedQty >= ExpectedQty      → Closed
    // Cancelled lines are left alone (the PO might have been
    // user-cancelled separately; we don't resurrect those).
    Task RevertLineStatusAsync(
        Guid purchaseOrderLineId,
        Guid? userId,
        CancellationToken ct = default);

    // Block 4.5.2 d.2.3.a (TD-026 closure prep) — single-line UPDATE.
    // SET covers ProductId / UomId / ExpectedQuantity + audit only;
    // never touches ReceivedQuantity, LineNumber, LineNo, DisplayOrder,
    // or Status. The exclusion is structural — even an operator-crafted
    // POST cannot mutate those fields through this method. UpdatedAt +
    // UpdatedBy stamped; Version += 1 for mutation tracking (no OCC
    // gate per the d.2.3 last-write-wins decision).
    //
    // The service is expected to have pre-checked the target line has
    // ReceivedQuantity == 0. This method does NOT enforce that — it
    // happily UPDATEs ExpectedQuantity on a received line, which the
    // invariants on inbound.ReceivingLines + Stock math do not allow.
    // Service-layer guard is authoritative.
    Task UpdateLineAsync(
        Guid lineId,
        Guid productId,
        Guid uomId,
        decimal expectedQuantity,
        Guid? userId,
        CancellationToken ct = default);

    // Block 4.5.2 d.2.3.a — single-line INSERT. ReceivedQuantity
    // hardcoded 0 + Status hardcoded 'Open' (new lines have no receipts
    // by definition; excluding both from the parameter set eliminates
    // the operator-mutation surface). LineNo computed inline as
    // LineNumber * 10 per chunk c precedent.
    Task InsertSingleLineAsync(
        Guid purchaseOrderId,
        int lineNumber,
        Guid productId,
        Guid uomId,
        decimal expectedQuantity,
        Guid? userId,
        CancellationToken ct = default);

    // Block 4.5.2 d.2.3.a — single-line DELETE. Service is expected
    // to have pre-checked ReceivedQuantity == 0. SQL Server FK NO
    // ACTION on inbound.ReceivingLines.PurchaseOrderLineId
    // (Migration_058) is the last-resort guard — a 547 thrown here
    // indicates an in-flight race the app layer can't fix (caller
    // catches + converts to a friendly error).
    Task DeleteLineAsync(Guid lineId, CancellationToken ct = default);
}
