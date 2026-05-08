using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.Products. Bound to a single tenant
// DB connection — get an instance via IProductRepositoryFactory.
//
// Phase 6B is read-only; Insert/Update/Archive belong to a future admin-
// CRUD surface (Phase 7+) and are intentionally absent — interface stays
// small, no untested write methods shipped.
public interface IProductRepository
{
    // List-page query. Returns the paged rows + total count in a single
    // QueryMultiple round-trip. Unknown SortBy keys fall back to Name ASC
    // via ProductSortMapper.
    Task<PagedResult<ProductListRow>> GetPagedAsync(
        ProductFilter filter, CancellationToken ct = default);

    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Detail-page resolution by SKU. Code is tenant-wide unique
    // (UQ index on master.Products.Code).
    Task<Product?> GetByCodeAsync(string code, CancellationToken ct = default);

    // Detail-page list-row read — same JOIN aggregate shape as
    // GetPagedAsync but for a single product. Returns a ProductListRow
    // (carries CategoryCode + StockOnHand) so the Detail page can fill
    // its hero stats and badges from a single round-trip without a
    // separate Stock query.
    Task<ProductListRow?> GetListRowByCodeAsync(string code, CancellationToken ct = default);
}
