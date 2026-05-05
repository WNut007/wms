namespace WMS.BLL.Services.Inbound;

// Input for IReceivingService.ReceiveStockAsync — the Receiving-α
// shape, lot- and pallet-less. Receiving-β extends this with optional
// Lot / Pallet identifiers; older callers keep working because both
// extensions are nullable on the wider type.
public sealed record ReceiveStockRequest(
    Guid LocationId,
    Guid ProductId,
    Guid OwnerId,
    Guid UomId,
    decimal Quantity);
