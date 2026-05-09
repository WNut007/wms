namespace WMS.DAL.Repositories.Counts;

// Closed-set whitelist (SQL-injection defence). Unknown keys fall
// through to c.StartedAt DESC.
public static class CycleCountSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "countnumber" => "c.CountNumber",
            "warehouse"   => "wh.Code",
            "status"      => "c.Status",
            "linecount"   => "agg.LineCount",
            "startedby"   => "u.FullName",
            "startedat"   => "c.StartedAt",
            _             => "c.StartedAt",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}

// Read-projection for the Detail Lines table. JOINs Products +
// Locations (resolved codes/names). Mirrors PurchaseOrderLineRow.
public sealed record CycleCountLineRow(
    Guid Id,
    int LineNumber,
    Guid StockId,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid LocationId,
    string LocationCode,
    string UomCode,
    string OwnerCode,
    string? LotNumber,
    string? PalletNumber,
    decimal ExpectedQuantity,
    decimal? CountedQuantity,
    string LineStatus,
    string? Notes);
