namespace WMS.Common.Inventory;

// Natural-key 6-tuple for inventory.Stock. LotId / PalletId are
// nullable because not every product (or every flow) tracks them —
// pick-by-piece consumables, for instance, may have neither.
//
// Uniqueness on this tuple is enforced at the service layer; the DB
// has a NON-UNIQUE composite index for fast lookups but composite
// UNIQUE in SQL Server isn't NULL-safe (two NULL values count as
// distinct). The service's upsert path uses NULL-safe SELECT before
// deciding INSERT vs version-checked UPDATE.
public sealed record StockKey(
    Guid LocationId,
    Guid ProductId,
    Guid? LotId,
    Guid? PalletId,
    Guid OwnerId,
    Guid UomId);
