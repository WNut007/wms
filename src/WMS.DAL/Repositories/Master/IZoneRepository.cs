using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Phase 30A.3 Block 2.1 — first appearance of this interface. Zones
// had no repo before (Phase 1 schema only). Bound to a single tenant
// DB connection via IZoneRepositoryFactory.
//
// Composite-key uniqueness: Zone.Code is unique within a warehouse,
// not tenant-wide. GetByCode-style lookups take (warehouseId, code).
public interface IZoneRepository
{
    // Active zones in a specific warehouse, ordered by Code. Used by
    // the Locations CRUD's cascade dropdown (Warehouse → Zone → Bin).
    Task<IReadOnlyList<LookupItem>> GetActiveByWarehouseAsync(
        Guid warehouseId, CancellationToken ct = default);

    Task<PagedResult<ZoneListRow>> GetPagedAsync(ZoneFilter f, CancellationToken ct = default);

    Task<ZoneStatusCounts> GetStatusCountsAsync(string? search, CancellationToken ct = default);

    Task<Zone?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Warehouse-scoped lookup — same Code can exist across warehouses.
    Task<Zone?> GetByCodeInWarehouseAsync(
        Guid warehouseId, string code, CancellationToken ct = default);

    Task<Guid> InsertAsync(Zone entity, Guid? userId, CancellationToken ct = default);

    Task<bool> UpdateAsync(Zone entity, Guid? userId, CancellationToken ct = default);
}
