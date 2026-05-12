namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for IZoneRepository
// .GetPagedAsync. Default sort: WarehouseCode then Code so zones
// group by their parent warehouse in the Index view.
public static class ZoneSortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]      = "z.Code",
            ["name"]      = "z.Name",
            ["type"]      = "z.Type",
            ["warehouse"] = "w.Code",
            ["updatedat"] = "z.UpdatedAt",
        };

    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        if (sortBy is null || !Map.TryGetValue(sortBy, out var column))
            return "w.Code ASC, z.Code ASC";

        return $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
