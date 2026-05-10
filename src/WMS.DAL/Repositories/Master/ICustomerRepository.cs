using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads + writes against master.Customers. Bound to a
// single tenant DB connection — get an instance via
// ICustomerRepositoryFactory.
//
// Writes added Phase 7. master.Customers has no Version column —
// UpdateAsync uses last-write-wins (D4). UpdateAsync omits Code AND
// CustomerType from SET: Code is read-only on Edit (FK-orphan risk),
// CustomerType is read-only because flipping B2B ↔ B2C would orphan
// the B2B-only fields (CompanyName / TaxId).
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

    // Inserts a new customer. Caller may pre-assign Id; if Empty, repo
    // generates one. Throws SqlException 2627 on duplicate Code.
    Task<Guid> InsertAsync(Customer entity, Guid? userId, CancellationToken ct = default);

    // Updates a customer. Code + CustomerType NOT in SET — see interface
    // comment. Returns rows-affected > 0.
    Task<bool> UpdateAsync(Customer entity, Guid? userId, CancellationToken ct = default);

    // Phase 14A — flat lookup (Id/Code/Name) of customers in 'Active'
    // status. Used to populate the Customer dropdown on SO Create.
    // Mirrors ProductRepository.GetActiveAsync.
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);
}
