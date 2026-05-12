using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Phase 7 introduced this repo as a dropdown lookup for Products.
// Phase 30A.3 Block 1.4 extends to full admin CRUD with hierarchy
// awareness:
//   * Path is maintained by trg_ProductCategories_MaintainPath
//     (migration 031) — BLL must NEVER set it on insert/update.
//   * trg_ProductCategories_NoCycles (migration 030) rejects cycles
//     in the parent chain via RAISERROR + ROLLBACK.
//   * GetPickerItemsAsync supports the Edit form by excluding self +
//     descendants from the parent dropdown.
public interface IProductCategoryRepository
{
    // Phase 7 dropdown projection — active categories, Path-ordered.
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);

    // ─── Phase 30A.3 admin CRUD surface ──────────────────────────────

    Task<PagedResult<ProductCategoryListRow>> GetPagedAsync(
        ProductCategoryFilter f, CancellationToken ct = default);

    Task<ProductCategoryStatusCounts> GetStatusCountsAsync(
        string? search, CancellationToken ct = default);

    Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ProductCategory?> GetByCodeAsync(string code, CancellationToken ct = default);

    // Parent-picker projection for Create + Edit forms.
    //   * On Create: pass excludeId=null → all active categories.
    //   * On Edit: pass excludeId=current row → filters out the row
    //     itself AND all descendants (Path prefix-match) so the operator
    //     can't pick an obvious cycle before the trigger catches it.
    Task<IReadOnlyList<ProductCategoryPickerItem>> GetPickerItemsAsync(
        Guid? excludeId, CancellationToken ct = default);

    Task<Guid> InsertAsync(ProductCategory entity, Guid? userId, CancellationToken ct = default);

    Task<bool> UpdateAsync(ProductCategory entity, Guid? userId, CancellationToken ct = default);
}
