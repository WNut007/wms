namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for ICarrierRepository
// .GetPagedAsync. Same shape as the other Master sort mappers.
//
// Default sort: Code ASC. 'displayorder' takes precedence over Code
// when explicitly requested — operators may want to see carrier
// selection priority order.
public static class CarrierSortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]         = "c.Code",
            ["name"]         = "c.Name",
            ["type"]         = "c.Type",
            ["status"]       = "c.Status",
            ["country"]      = "c.Country",
            ["displayorder"] = "c.DisplayOrder",
            ["updatedat"]    = "c.UpdatedAt",
        };

    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var column = sortBy is not null && Map.TryGetValue(sortBy, out var c)
            ? c
            : "c.Code";
        return $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
