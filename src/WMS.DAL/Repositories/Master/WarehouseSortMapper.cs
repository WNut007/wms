namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for IWarehouseRepository
// .GetPagedAsync. Same shape as ProductSortMapper / CustomerSortMapper.
//
// Intentionally absent: 'region'. The mock UI exposed a Region facet
// not backed by any schema column (Phase 6B drops it — see brief §4
// sub-decisions). Frontend dropdowns may keep emitting it; the mapper
// absorbs it and falls through to Name ASC.
public static class WarehouseSortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]      = "w.Code",
            ["name"]      = "w.Name",
            ["type"]      = "w.Type",
            // Alias from the SELECT list — references the LEFT JOIN
            // aggregate, not a base column.
            ["locations"] = "LocationCount",
            ["updatedat"] = "w.UpdatedAt",
        };

    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var column = sortBy is not null && Map.TryGetValue(sortBy, out var c)
            ? c
            : "w.Name";
        return $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
