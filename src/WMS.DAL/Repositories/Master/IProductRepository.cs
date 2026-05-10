using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads + writes against master.Products. Bound to a
// single tenant DB connection — get an instance via
// IProductRepositoryFactory.
//
// Writes added in Phase 7 (admin Create / Edit). master.Products has no
// Version column (low-churn metadata; ADR-014 audit confirms optimistic
// concurrency intentionally not used) — UpdateAsync therefore does NOT
// include `Version = Version + 1` or `WHERE Version = @originalVersion`.
// Concurrent edits are last-write-wins; acceptable for master metadata.
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

    // Inserts a new product. Caller may pre-assign Id; if Guid.Empty,
    // the repo generates one. CreatedAt is set to SYSUTCDATETIME() on
    // the server so all rows share a consistent clock. CreatedBy comes
    // from the controller's CurrentUser.UserId (or null for system
    // writes — same convention as PurchaseOrderRepository.CreateAsync).
    //
    // Throws SqlException 2627 (UNIQUE KEY violation) if Code is already
    // taken — the controller catches this and surfaces a field-level
    // ModelState error on Code.
    Task<Guid> InsertAsync(Product entity, Guid? userId, CancellationToken ct = default);

    // Updates a product. Code is NOT updated (read-only on the Edit
    // form per Phase 7 brief — changing it would orphan FK references
    // from inventory.Stock, billing, etc.). UpdatedAt set server-side;
    // UpdatedBy from controller's CurrentUser.UserId. No Version
    // increment (no Version column on master.Products).
    Task<bool> UpdateAsync(Product entity, Guid? userId, CancellationToken ct = default);

    // Phase 9A — flat lookup for dropdown sources (PO line grid, etc.).
    // Filters to Status='Active' (Inactive/Discontinued/Draft excluded
    // — operator shouldn't add a discontinued SKU to a fresh order).
    // Ordered by Code ASC.
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default);

    // Phase 18 — bulk projection for the mobile Receive task page.
    // Returns Code + Name + TrackingMethod for the given product ids
    // so the per-line cards can render product labels + flag serial-
    // tracked products without per-line round-trips.
    // TrackingMethod ∈ {'None', 'Lot', 'LotAndSerial'} per migration 014.
    Task<IReadOnlyDictionary<Guid, ProductLineMeta>> GetMetaByIdsAsync(
        IEnumerable<Guid> productIds, CancellationToken ct = default);
}

// Phase 18 — projection returned by IProductRepository.GetMetaByIdsAsync.
// Keeps the receive task page's per-line render to one bulk query.
public sealed record ProductLineMeta(
    string Code,
    string Name,
    string TrackingMethod);
