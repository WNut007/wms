namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for IUomRepository
// .GetPagedAsync. Same shape as BoxTypeSortMapper / WarehouseSortMapper.
public static class UomSortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]      = "u.Code",
            ["name"]      = "u.Name",
            ["type"]      = "u.Type",
            ["isbase"]    = "u.IsBase",
            ["updatedat"] = "u.UpdatedAt",
        };

    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var column = sortBy is not null && Map.TryGetValue(sortBy, out var c)
            ? c
            : "u.Code";
        return $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
