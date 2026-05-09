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
