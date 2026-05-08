namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for ICustomerRepository
// .GetPagedAsync. Same shape as ProductSortMapper — closed-set map IS
// the SQL-injection defence.
//
// Intentionally absent from the whitelist: 'totalorders', 'lastorderdate'.
// The mock UI exposed these as sort options, but there's no orders
// schema yet (TD-011); the controller stubs both fields to "—" / null,
// so a real ORDER BY on them would either need a non-existent column or
// silently no-op. Falling through to Name ASC keeps the page useful
// instead of broken. Frontend dropdowns can keep emitting those values;
// the whitelist absorbs them.
public static class CustomerSortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]    = "Code",
            ["name"]    = "Name",
            ["type"]    = "CustomerType",
            ["country"] = "Country",
        };

    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var column = sortBy is not null && Map.TryGetValue(sortBy, out var c)
            ? c
            : "Name";
        return $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
