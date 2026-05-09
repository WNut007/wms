using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.Locations. Bound to a single
// tenant DB connection — get an instance via ILocationRepositoryFactory.
//
// Phase 9B surface is dropdown-only — populates the Location <select>
// per line on the GR Create form. Active locations within the
// requesting warehouse only.
public interface ILocationRepository
{
    // Active locations in a specific warehouse, ordered by Code.
    // Filters: WarehouseId = @warehouseId AND IsActive = 1 AND
    // Status = 'Active' (mirrors ReceiveController's existing inline
    // resolver shape).
    Task<IReadOnlyList<LookupItem>> GetActiveByWarehouseAsync(
        Guid warehouseId, CancellationToken ct = default);
}
