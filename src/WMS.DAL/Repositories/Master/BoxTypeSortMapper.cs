namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for IBoxTypeRepository
// .GetPagedAsync. Same shape as WarehouseSortMapper.
//
// Unknown/hostile sortBy falls through to Code ASC — defensive against
// SQL-injection attempts via the wire field.
public static class BoxTypeSortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]      = "b.Code",
            ["name"]      = "b.Name",
            ["weight"]    = "b.EmptyWeightKg",
            ["cost"]      = "b.UnitCost",
            ["updatedat"] = "b.UpdatedAt",
        };

    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var column = sortBy is not null && Map.TryGetValue(sortBy, out var c)
            ? c
            : "b.Code";
        return $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
