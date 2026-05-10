using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

// Phase 14D — tenant-scoped reads against master.BoxTypes. Bound to a
// single tenant DB connection — get an instance via IBoxTypeRepository-
// Factory.
//
// MVP surface is dropdown-only — populates the BoxTypeId <select> on
// the Pack Detail submit form. Full BoxType admin CRUD is deferred.
public interface IBoxTypeRepository
{
    // Active box types ordered by Code (deterministic, picker-friendly).
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);
}
