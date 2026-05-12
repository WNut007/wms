namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for IProductCategoryRepository
// .GetPagedAsync. Default sort: Path ASC so hierarchical subtrees stay
// visually adjacent in the list view ('/Skincare/Cream' lands next to
// '/Skincare/Lotion').
public static class ProductCategorySortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]         = "c.Code",
            ["name"]         = "c.Name",
            ["path"]         = "c.Path",
            ["displayorder"] = "c.DisplayOrder",
            ["updatedat"]    = "c.UpdatedAt",
        };

    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var column = sortBy is not null && Map.TryGetValue(sortBy, out var c)
            ? c
            : "c.Path";
        // NULL Path sinks to end via the CASE — categories without a
        // computed Path show up last rather than at the top.
        return column == "c.Path"
            ? $"CASE WHEN c.Path IS NULL THEN 1 ELSE 0 END, c.Path {(desc ? "DESC" : "ASC")}, c.Name"
            : $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
