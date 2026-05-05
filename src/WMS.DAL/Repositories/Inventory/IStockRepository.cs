using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Tenant-scoped read API for inventory.Stock. Mutation primitives
// (receive / putaway / adjust / reserve / pick) live in their own
// repos / services per ADR — the read methods here exist independent
// of any of those flows.
public interface IStockRepository
{
    Task<Stock?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Stock>> GetByLocationAsync(Guid locationId, CancellationToken ct = default);
    Task<IReadOnlyList<Stock>> GetByProductAsync(Guid productId, CancellationToken ct = default);

    // NULL-safe lookup on the 6-tuple key — see implementation for
    // the SQL pattern that handles nullable LotId / PalletId.
    Task<Stock?> GetByKeyAsync(StockKey key, CancellationToken ct = default);
}
