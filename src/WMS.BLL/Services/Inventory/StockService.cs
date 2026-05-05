using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inventory;

// Thin wrapper over IStockRepositoryFactory + the per-call repo. Queries
// only — mutation primitives (receive / putaway / adjust / reserve / pick)
// live in their own services. The Available filter is the only piece of
// "logic" here today; everything else delegates straight to Dapper.
public sealed class StockService : IStockService
{
    private readonly IStockRepositoryFactory _repoFactory;

    public StockService(IStockRepositoryFactory repoFactory) =>
        _repoFactory = repoFactory;

    public Task<Stock?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Stock>> GetByLocationAsync(
        Guid tenantId, Guid locationId, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByLocationAsync(locationId, ct);

    public Task<IReadOnlyList<Stock>> GetByProductAsync(
        Guid tenantId, Guid productId, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByProductAsync(productId, ct);

    public Task<Stock?> GetByKeyAsync(
        Guid tenantId, StockKey key, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByKeyAsync(key, ct);

    public async Task<IReadOnlyList<Stock>> GetAvailableByProductAsync(
        Guid tenantId, Guid productId, CancellationToken ct = default)
    {
        // Filter happens in C# rather than SQL because the DB doesn't
        // store the computed Available; pushing the predicate down would
        // mean teaching the repo about it, which it shouldn't know.
        var rows = await _repoFactory.For(tenantId).GetByProductAsync(productId, ct);
        return rows.Where(s => s.QuantityAvailable > 0).ToList();
    }
}
