namespace WMS.DAL.Repositories.Master;

// Whitelisted sort-key → SQL column resolver for ILocationRepository
// .GetPagedAsync. Default sort: WarehouseCode → ZoneCode → BinRank so
// locations group hierarchically and within-zone they appear in
// putaway/picking priority order.
public static class LocationSortMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]      = "l.Code",
            ["warehouse"] = "w.Code",
            ["zone"]      = "z.Code",
            ["binrank"]   = "l.BinRank",
            ["status"]    = "l.Status",
            ["updatedat"] = "l.UpdatedAt",
        };

    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        if (sortBy is null || !Map.TryGetValue(sortBy, out var column))
            return "w.Code ASC, z.Code ASC, l.BinRank ASC, l.Code ASC";

        return $"{column} {(desc ? "DESC" : "ASC")}";
    }
}
