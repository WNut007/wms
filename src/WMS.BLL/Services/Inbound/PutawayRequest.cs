using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inbound;

// Service-level shape for IPutawayService.PutawayStockAsync. The
// caller has already resolved natural-key codes (Product / Lot /
// Pallet / Owner / Location) into the StockKey on the From side.
public sealed record PutawayRequest(
    StockKey FromKey,
    Guid ToLocationId,
    decimal Quantity);

// Both sides of the move after the operation completes — useful for
// the controller's success view (shows source remaining + destination
// new total without a second round-trip).
public sealed record PutawayResult(Stock Source, Stock Destination);
