using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Phase 7 introduced this repo as a dropdown lookup for Products.
// Phase 30A.3 Block 1 extends to full admin CRUD.
//
// Bound to a single tenant DB connection via IUomRepositoryFactory.
public interface IUomRepository
{
    // Dropdown projection (Phase 7) — active UoMs ordered by Code.
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);

    // ─── Phase 30A.3 admin CRUD surface ──────────────────────────────

    Task<PagedResult<UomListRow>> GetPagedAsync(UomFilter f, CancellationToken ct = default);

    Task<UomStatusCounts> GetStatusCountsAsync(string? search, CancellationToken ct = default);

    Task<Uom?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Uom?> GetByCodeAsync(string code, CancellationToken ct = default);

    // Returns the UoM marked IsBase=1 for the given Type, or null if
    // none. Used by the "one base per Type" validator rule. exceptId
    // allows the Edit form to ignore the row being edited.
    Task<Uom?> GetBaseByTypeAsync(string type, Guid? exceptId, CancellationToken ct = default);

    Task<Guid> InsertAsync(Uom entity, Guid? userId, CancellationToken ct = default);

    Task<bool> UpdateAsync(Uom entity, Guid? userId, CancellationToken ct = default);
}
