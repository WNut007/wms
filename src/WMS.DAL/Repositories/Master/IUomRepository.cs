using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.UnitsOfMeasure. Bound to a single
// tenant DB connection — get an instance via IUomRepositoryFactory.
//
// Phase 7 surface is dropdown-only — populates the BaseUomId <select>
// on Product Create / Edit forms. Full UoM admin CRUD is deferred.
public interface IUomRepository
{
    // Active UoMs ordered by Code (deterministic, picker-friendly).
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);
}
