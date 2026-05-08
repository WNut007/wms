namespace WMS.BLL.Services.Inbound;

// Input for IReceivingService.ReceiveStockAsync.
//
// Lot and Pallet are optional — pass them when the inbound paperwork
// names a lot / LPN. The service upserts the corresponding row first
// (creates if new, finds if already known) and threads the resulting
// Id into the 6-tuple StockKey. Receiving-α call sites that don't pass
// either parameter keep working unchanged.
public sealed record ReceiveStockRequest(
    Guid LocationId,
    Guid ProductId,
    Guid OwnerId,
    Guid UomId,
    decimal Quantity,
    LotInfo? Lot = null,
    PalletInfo? Pallet = null,
    // Optional ReceivingLine.Id when this receive is part of an
    // orchestrated header/line flow. Threaded into StockMovements as
    // (ReferenceType='ReceivingLine', ReferenceId=<this guid>) so the
    // movement traces back to its line. Direct receives (no header)
    // pass null and the movement carries no reference.
    Guid? ReceivingLineId = null);

// Lot identifiers + dates supplied at receive time. Existing lots
// (re-received after partial outbound) keep the dates they were
// originally recorded with; supplying different dates on a known
// LotNumber is a no-op.
public sealed record LotInfo(
    string LotNumber,
    DateOnly ReceivedDate,
    DateOnly? ExpiryDate = null);

// Pallet (LPN) identifier supplied at receive time. Status / lifecycle
// columns are managed elsewhere; here we only resolve / register the
// number.
public sealed record PalletInfo(string PalletNumber);
