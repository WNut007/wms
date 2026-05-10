namespace WMS.DAL.Repositories.Inventory;

// Phase 22 — read-projections for the mobile Locate utility. Two row
// shapes: per-location (item view = "where is this product?") and
// per-product (location view = "what's at this bin?"). Fields differ
// because each emphasises the OTHER axis (item view shows location
// metadata per row; location view shows product metadata per row).
//
// Both share the JOIN backbone (Stock + Locations + Zones + Products
// + Owners + UoMs + LEFT JOIN Lots + Pallets) — only the SELECT columns
// + ORDER BY differ.

// Per-product multi-location row. One per Stock entry where the
// ProductId matches. ZoneType drives the status badge color in the
// mobile card (Storage = purple, Receiving/Staging = blue, etc.).
// LotAgeDays computed server-side via DATEDIFF for stable rendering.
public sealed record LocateItemRow(
    Guid StockId,
    decimal QuantityOnHand,
    decimal QuantityAllocated,
    Guid LocationId,
    string LocationCode,
    string ZoneCode,
    string ZoneType,
    string OwnerCode,
    Guid? LotId,
    string? LotNumber,
    int? LotAgeDays,
    DateTime? ExpiryDate,
    Guid? PalletId,
    string? PalletNumber,
    string UomCode,
    DateTime CreatedAt);

// Per-location items row. One per Stock entry where the LocationId
// matches. Same shape as the item view but emphasises the product
// metadata (Code + Name + UoM) and drops Location/Zone fields (those
// are the parent of the page).
public sealed record LocateLocationRow(
    Guid StockId,
    decimal QuantityOnHand,
    decimal QuantityAllocated,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string OwnerCode,
    Guid? LotId,
    string? LotNumber,
    int? LotAgeDays,
    DateTime? ExpiryDate,
    Guid? PalletId,
    string? PalletNumber,
    string UomCode,
    DateTime CreatedAt);
