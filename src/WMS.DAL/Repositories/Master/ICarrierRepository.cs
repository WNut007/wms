using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.Carriers. Bound to a single tenant
// DB connection — get an instance via ICarrierRepositoryFactory.
//
// Phase 7 surface is dropdown-only — populates the optional
// PreferredCarrierId <select> on Customer Create / Edit forms. Full
// Carrier admin CRUD (status lifecycle, rate cards) is deferred.
public interface ICarrierRepository
{
    // Carriers safe to surface as a customer's preferred carrier:
    // Status='Production' AND IsActive=1. Inactive / Configured / Tested
    // carriers aren't ready for production assignment.
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);
}
