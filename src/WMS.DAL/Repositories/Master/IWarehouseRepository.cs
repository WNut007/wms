using WMS.Common.Auth;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.Warehouses. Bound to a single
// tenant DB connection — get an instance via IWarehouseRepositoryFactory.
public interface IWarehouseRepository
{
    // Active warehouses only, ordered by Code (deterministic + matches
    // IX_Warehouses_Active index leading column for cheap reads).
    Task<IReadOnlyList<WarehouseInfo>> GetActiveAsync(CancellationToken ct = default);
}
