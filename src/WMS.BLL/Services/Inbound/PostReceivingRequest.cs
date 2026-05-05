namespace WMS.BLL.Services.Inbound;

// Service-level shape for IReceivingHeaderService.PostReceivingAsync.
// PurchaseOrderId on the header and PurchaseOrderLineId on each line
// are independent — a PO-tied receipt can have one line not on the
// PO ("extra"), and a blind-receipt header can still be the right
// home for those lines.
public sealed record PostReceivingRequest(
    string ReceivingNumber,
    Guid? PurchaseOrderId,
    Guid WarehouseId,
    DateTime? ReceivedAt,
    string? Notes,
    IReadOnlyList<PostReceivingLineRequest> Lines);

// One physical line: what was received, where it landed, optional
// lot / pallet identifiers (passed through to β's GetOrCreate
// primitives), and an optional PO line link to bump.
public sealed record PostReceivingLineRequest(
    int LineNumber,
    Guid? PurchaseOrderLineId,
    Guid ProductId,
    Guid UomId,
    Guid OwnerId,
    Guid LocationId,
    decimal ReceivedQuantity,
    LotInfo? Lot = null,
    PalletInfo? Pallet = null);
