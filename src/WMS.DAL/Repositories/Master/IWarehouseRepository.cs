using WMS.Common.Auth;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.Warehouses. Bound to a single
// tenant DB connection — get an instance via IWarehouseRepositoryFactory.
//
// Two distinct projections, by design:
//   - GetActiveAsync → WarehouseInfo (Id, Code, Name) — used only by
//     the Step 3 tenant-warehouse picker on login.
//   - GetPagedAsync / GetByCode / GetById → Warehouse / WarehouseListRow
//     — used by the /Warehouses list + detail UI starting Phase 6B.
//
// Phase 6B is read-only; Insert/Update/Archive belong to Phase 7+
// admin CRUD.
public interface IWarehouseRepository
{
    // Active warehouses only, ordered by Code (deterministic + matches
    // IX_Warehouses_Active index leading column for cheap reads). Used
    // by the login warehouse picker — kept on the lighter WarehouseInfo
    // projection so Step 3 stays fast.
    Task<IReadOnlyList<WarehouseInfo>> GetActiveAsync(CancellationToken ct = default);

    // List-page query. LEFT JOIN aggregate over master.Locations for a
    // live LocationCount. Returns paged rows + total count in one
    // QueryMultiple round-trip. Unknown SortBy keys fall back to Name
    // ASC via WarehouseSortMapper.
    Task<PagedResult<WarehouseListRow>> GetPagedAsync(
        WarehouseFilter filter, CancellationToken ct = default);

    Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Detail-page resolution by Code. Code is tenant-wide unique
    // (UQ index on master.Warehouses.Code).
    Task<Warehouse?> GetByCodeAsync(string code, CancellationToken ct = default);

    // Detail-page list-row read — same JOIN-aggregate shape as
    // GetPagedAsync but for one row. Returns the LocationCount
    // alongside the entity fields the Detail page needs, so the page
    // renders from a single round-trip without a separate count query.
    Task<WarehouseListRow?> GetListRowByCodeAsync(string code, CancellationToken ct = default);

    // Inserts a new warehouse. Caller may pre-assign Id; if Empty, repo
    // generates one. Throws SqlException 2627 on duplicate Code.
    Task<Guid> InsertAsync(Warehouse entity, Guid? userId, CancellationToken ct = default);

    // Updates a warehouse. Code NOT in SET — read-only on Edit (FK
    // orphan risk: Code appears in inventory.Stock paths, location
    // displays, audit trails). IsActive IS updatable from the Edit
    // form (no separate "archive" button per the brief — toggle the
    // checkbox). Returns rows-affected > 0.
    Task<bool> UpdateAsync(Warehouse entity, Guid? userId, CancellationToken ct = default);
}
