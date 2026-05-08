namespace WMS.DAL.Repositories.Master;

// Inputs to IProductRepository.GetPagedAsync. All filter fields are
// nullable (NULL = no filter); Page/PageSize default to the same shape
// the mock list service used so the JSON contract on the wire is
// unchanged.
//
// SortBy values are mapped to SQL columns by ProductSortMapper — never
// substituted raw, so a hostile sortBy can't break out into the ORDER BY
// clause.
public sealed record ProductFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    string? Status = null,
    string? CategoryCode = null,
    string? Brand = null,
    string? SortBy = null,
    bool SortDesc = false);
