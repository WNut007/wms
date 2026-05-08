using WMS.Common.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Read-only projection for the Activity tab feed
// (IStockMovementRepository.GetByProductAsync). Carries the resolved
// PerformedByName + From/To location codes that the StockMovement
// entity itself doesn't (and shouldn't) hold — same separation as
// ProductListRow vs Product, WarehouseListRow vs Warehouse.
//
// Names resolve via LEFT JOINs in the repo SQL: NULL flows through
// when the FK is NULL (Receive has FromLocationId=NULL by design),
// when the referenced row is missing (defensive), or for system
// actions (PerformedBy=NULL → both u.* NULL → COALESCE picks the
// 'System' literal).
//
// Drops the entity's StockId / UomId / OwnerId / ReferenceId / IDs
// from this projection — none surfaces in the panel and YAGNI on
// adding them back. The richer GetByStockAsync / GetByReferenceAsync
// still return the full entity if a future caller needs IDs.
public sealed record StockMovementListRow(
    Guid Id,
    StockMovementType MovementType,
    decimal QuantityDelta,
    string? FromLocationCode,
    string? ToLocationCode,
    string? ReferenceType,
    string? Notes,
    string PerformedByName,
    DateTime PerformedAt);
