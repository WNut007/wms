using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Phase 7 introduced this repo as a Production-only dropdown lookup.
// Phase 30A.3 Block 1.3 extends to full admin CRUD with broader reads.
//
// Bound to a single tenant DB connection via ICarrierRepositoryFactory.
public interface ICarrierRepository
{
    // Phase 7 dropdown projection — Production AND IsActive=1.
    // KEEP filter intact — Phase 14E PO Vendor + Customer.PreferredCarrierId
    // consume this. Admin Index uses GetPagedAsync (no filter) instead.
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);

    // ─── Phase 30A.3 admin CRUD surface ──────────────────────────────

    Task<PagedResult<CarrierListRow>> GetPagedAsync(CarrierFilter f, CancellationToken ct = default);

    Task<CarrierStatusCounts> GetStatusCountsAsync(string? search, CancellationToken ct = default);

    Task<Carrier?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Carrier?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<Guid> InsertAsync(Carrier entity, Guid? userId, CancellationToken ct = default);

    Task<bool> UpdateAsync(Carrier entity, Guid? userId, CancellationToken ct = default);
}
