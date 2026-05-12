using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Phase 14D introduced this repo as a dropdown lookup for Pack tasks.
// Phase 30A.3 Block 1 extends to full admin CRUD.
//
// Bound to a single tenant DB connection via IBoxTypeRepositoryFactory.
public interface IBoxTypeRepository
{
    // Dropdown projection (Phase 14D) — active types ordered by Code.
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);

    // ─── Phase 30A.3 admin CRUD surface ──────────────────────────────

    Task<PagedResult<BoxTypeListRow>> GetPagedAsync(BoxTypeFilter f, CancellationToken ct = default);

    Task<BoxTypeStatusCounts> GetStatusCountsAsync(string? search, CancellationToken ct = default);

    Task<BoxType?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<BoxType?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<Guid> InsertAsync(BoxType entity, Guid? userId, CancellationToken ct = default);

    Task<bool> UpdateAsync(BoxType entity, Guid? userId, CancellationToken ct = default);
}
