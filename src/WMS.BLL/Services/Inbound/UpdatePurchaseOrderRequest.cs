namespace WMS.BLL.Services.Inbound;

// Phase 9A — Edit form payload. Header-only (ExpectedDate + Notes —
// PoNumber/OwnerId/WarehouseId frozen post-create) plus optional Lines
// replacement. ReplaceLines = true triggers a full DELETE + INSERT
// transaction; the service rejects that path when any line on the PO
// already has ReceivedQuantity > 0.
//
// When ReplaceLines = false, Lines is ignored and only the header
// updates — used by the Edit form's "lines locked" path (header-only
// edit when receipts exist).
public sealed record UpdatePurchaseOrderRequest(
    DateOnly? ExpectedDate,
    string? Notes,
    bool ReplaceLines,
    IReadOnlyList<UpdatePurchaseOrderLineRequest> Lines);

public sealed record UpdatePurchaseOrderLineRequest(
    int LineNumber,
    Guid ProductId,
    Guid UomId,
    decimal ExpectedQuantity);

// Block 4.5.2 d.2.3 (TD-026 closure prep) — surgical partial update.
// Replaces the wholesale ReplaceLinesAsync flow for Edit POST so per-
// line locked state can be preserved across saves: locked lines stay
// alone, unlocked lines editable, new lines insertable on POs that
// already have receipts. d.2.3.a wires the service+controller; d.2.3.b
// switches the Razor + JS to render locked rows per-row.
//
// Server-authoritative invariants enforced inside the service:
//   LineUpdates  — target line must exist AND ReceivedQuantity == 0.
//                  UPDATE column-set never includes ReceivedQuantity,
//                  LineNumber, LineNo, DisplayOrder, or Status — even
//                  a tampered POST cannot mutate those.
//   LineDeletes  — target line must exist AND ReceivedQuantity == 0.
//                  DB FK NO ACTION on ReceivingLines.PurchaseOrderLineId
//                  is the last-resort guard for in-flight receipt
//                  races; service pre-check surfaces a friendly error.
//   LineInserts  — LineNumber must not collide with existing or among
//                  inserts; ReceivedQuantity hardcoded 0 in INSERT
//                  (operator-supplied value not in the parameter set).
//
// All ops + header update wrapped in a single TransactionScope —
// mixed-batch atomicity, any failure rolls back every write.
public sealed record PartialUpdatePurchaseOrderRequest(
    DateOnly? ExpectedDate,
    string? Notes,
    IReadOnlyList<PartialUpdateLineEdit> LineUpdates,
    IReadOnlyList<PartialUpdateLineInsert> LineInserts,
    IReadOnlyList<Guid> LineDeletes);

public sealed record PartialUpdateLineEdit(
    Guid LineId,
    Guid ProductId,
    Guid UomId,
    decimal ExpectedQuantity);

public sealed record PartialUpdateLineInsert(
    int LineNumber,
    Guid ProductId,
    Guid UomId,
    decimal ExpectedQuantity);
