using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.ProductCategories. Bound to a
// single tenant DB connection — get an instance via
// IProductCategoryRepositoryFactory.
//
// Phase 7 surface is dropdown-only — populates the CategoryId <select>
// on Product Create / Edit forms. Full Categories admin CRUD (tree
// editor) is deferred to a later phase.
public interface IProductCategoryRepository
{
    // Active categories ordered by Path so hierarchical groupings stay
    // adjacent in the dropdown ('/Electronics/Mobile/Phones' lands next
    // to '/Electronics/Mobile/Tablets'). NULL Path falls to end.
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);
}
