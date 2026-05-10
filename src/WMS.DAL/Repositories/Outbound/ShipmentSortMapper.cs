namespace WMS.DAL.Repositories.Outbound;

// Phase 15A — closed-set whitelist mapping wire SortBy keys → SQL
// columns. SQL-injection defence; mirrors PickTask/PackTaskSortMapper.
public static class ShipmentSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "shipmentnumber" => "s.ShipmentNumber",
            "sonumber"       => "so.SoNumber",
            "customer"       => "c.Code",
            "status"         => "s.Status",
            "carrier"        => "s.CarrierName",
            "cartoncount"    => "agg.CartonCount",
            "generatedat"    => "s.GeneratedAt",
            "shippedat"      => "s.ShippedAt",
            "cancelledat"    => "s.CancelledAt",
            _                => "s.GeneratedAt",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}
