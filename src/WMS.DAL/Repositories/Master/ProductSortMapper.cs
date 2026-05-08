namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for IProductRepository
// .GetPagedAsync. The closed map IS the SQL-injection defence: only the
// values in this dictionary can land inside an ORDER BY clause, so a
// hostile @sortBy parameter can't escape into arbitrary SQL.
//
// Keys mirror the wire-level sort values used by the existing
// /Products?sortBy=... routes (so the mock-era frontend keeps working
// after the controller swap in T8). Unknown / null sort keys fall back
// to Name ASC — the same default the mock service used.
public static class ProductSortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]      = "p.Code",
            ["name"]      = "p.Name",
            ["brand"]     = "p.Brand",
            ["category"]  = "c.Code",
            // Alias from the SELECT list — references the LEFT JOIN
            // aggregate, not a base column.
            ["stock"]     = "StockOnHand",
            ["updatedat"] = "p.UpdatedAt",
        };

    // Returns "{column} ASC" or "{column} DESC". Always safe to splice
    // directly into SQL — both the column expression and the direction
    // come from a closed set of trusted strings.
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var column = sortBy is not null && Map.TryGetValue(sortBy, out var c)
            ? c
            : "p.Name";
        return $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
