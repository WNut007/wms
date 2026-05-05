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
}
