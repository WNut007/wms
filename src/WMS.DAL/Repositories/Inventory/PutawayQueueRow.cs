namespace WMS.DAL.Repositories.Inventory;

// Phase 20 — read-projection for the mobile Putaway queue. One row per
// inventory.Stock entry sitting at a Receiving / Staging-zone location
// with positive OnHand. JOINs Locations + Zones + Products + Owners +
// Lots + Pallets so the mobile card can render natural-key codes
// without per-row lookups.
//
// Note: the spec assumed `master.Locations.IsStaging` (does not exist).
// Reality: `master.Zones.Type IN ('Receiving', 'Staging')` — same shape
// of spec rename as Phase 18 (IsSerialTracked → TrackingMethod) and
// Phase 19 (LotOnly → Lot). Applied silently per spec audit.
public sealed record PutawayQueueRow(
    Guid StockId,
    decimal QuantityOnHand,
    Guid LocationId,
    string LocationCode,
    string ZoneType,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string TrackingMethod,
    Guid OwnerId,
    string OwnerCode,
    Guid? LotId,
    string? LotNumber,
    Guid? PalletId,
    string? PalletNumber,
    Guid UomId,
    string UomCode,
    DateTime? LastMovementAt,
    DateTime CreatedAt);

// Phase 20 — suggested-location output for the mobile Putaway task page.
// One result (top scoring candidate) per call; the task page renders
// the location prominently with a list of reasons. Null when no
// Storage-zone location qualifies (operator falls back to the override
// scan area).
//
// Reasons are pre-formatted strings (e.g. "Same product nearby (3 rows)",
// "Low bin rank (15)") so the view doesn't need to know the scoring
// rules. Capacity-aware scoring is a TD — no per-location current vs
// max comparison until product-volume data lands.
public sealed record SuggestedLocationResult(
    Guid LocationId,
    string LocationCode,
    string ZoneCode,
    string ZoneName,
    int? BinRank,
    bool IsPickface,
    int SameProductRowCount,
    IReadOnlyList<string> Reasons);
