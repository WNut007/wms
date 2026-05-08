using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.Customers. Bound to a single
// tenant DB connection — get an instance via ICustomerRepositoryFactory.
//
// Phase 6B is read-only. Insert/Update/Archive belong to Phase 7+ admin
// CRUD.
public interface ICustomerRepository
{
    // List-page query. Returns paged rows + total count in one
    // QueryMultiple round-trip. Unknown SortBy keys fall back to
    // Name ASC via CustomerSortMapper.
    Task<PagedResult<CustomerListRow>> GetPagedAsync(
        CustomerFilter filter, CancellationToken ct = default);

    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Detail-page resolution by Code. Code is tenant-wide unique
    // (UQ index on master.Customers.Code).
    Task<Customer?> GetByCodeAsync(string code, CancellationToken ct = default);
}
