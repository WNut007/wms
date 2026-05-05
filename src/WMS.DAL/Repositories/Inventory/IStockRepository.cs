using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Tenant-scoped API for inventory.Stock. Reads are split-clean from the
// upsert primitive below; richer mutation primitives (putaway, pick,
// adjust, reserve) live in their own repos / services per ADR.
public interface IStockRepository
{
    Task<Stock?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Stock>> GetByLocationAsync(Guid locationId, CancellationToken ct = default);
    Task<IReadOnlyList<Stock>> GetByProductAsync(Guid productId, CancellationToken ct = default);

    // NULL-safe lookup on the 6-tuple key — see implementation for
    // the SQL pattern that handles nullable LotId / PalletId.
    Task<Stock?> GetByKeyAsync(StockKey key, CancellationToken ct = default);

    // Atomic receive primitive. Adds quantityDelta to the stock row at
    // the 6-tuple key, creating it on first arrival. Implemented as a
    // single MERGE WITH (HOLDLOCK) so concurrent receives at the same
    // key serialize safely without app-level locking. quantityDelta is
    // expected to be positive (receiving deposits stock, never removes
    // it); the service layer enforces that.
    Task<Stock> UpsertOnHandAsync(
        StockKey key,
        decimal quantityDelta,
        Guid? userId,
        CancellationToken ct = default);

    // Atomic put-away primitive. Decrements OnHand at the source stock
    // row and adds the same quantity onto the row at (toLocationId,
    // sourceProduct, sourceLot, sourcePallet, sourceOwner, sourceUom),
    // creating the destination row if it doesn't yet exist.
    //
    // SQL Server raises (THROW 50001..50003) if:
    //   * source row is missing
    //   * destination location matches source (no-op refused)
    //   * source has insufficient quantity
    //
    // Returns the source + destination rows after the operation.
    Task<(Stock Source, Stock Destination)> TransferStockAsync(
        Guid fromStockId,
        Guid toLocationId,
        decimal quantity,
        Guid? userId,
        CancellationToken ct = default);
}
