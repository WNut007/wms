using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Phase 9B introduced this repo as a dropdown lookup for GR Create.
// Phase 30A.3 Block 2.2 extends to full admin CRUD.
//
// Bound to a single tenant DB connection via ILocationRepositoryFactory.
public interface ILocationRepository
{
    // Phase 9B dropdown projection — WHERE WarehouseId = @warehouseId
    // AND IsActive = 1 AND Status = 'Active'. KEEP filter intact —
    // ReceiveController + GoodsReceiptController consume this.
    Task<IReadOnlyList<LookupItem>> GetActiveByWarehouseAsync(
        Guid warehouseId, CancellationToken ct = default);

    // ─── Phase 30A.3 admin CRUD surface ──────────────────────────────

    Task<PagedResult<LocationListRow>> GetPagedAsync(LocationFilter f, CancellationToken ct = default);

    Task<LocationStatusCounts> GetStatusCountsAsync(string? search, CancellationToken ct = default);

    Task<Location?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Warehouse-scoped uniqueness — same Code can exist across warehouses.
    Task<Location?> GetByCodeInWarehouseAsync(
        Guid warehouseId, string code, CancellationToken ct = default);

    // Cascade picker support: zones in a specific warehouse for the
    // form's Warehouse → Zone dropdown chain.
    Task<IReadOnlyList<LookupItem>> GetZonesForWarehouseAsync(
        Guid warehouseId, CancellationToken ct = default);

    Task<Guid> InsertAsync(Location entity, Guid? userId, CancellationToken ct = default);

    Task<bool> UpdateAsync(Location entity, Guid? userId, CancellationToken ct = default);
}
