namespace WMS.DAL.Repositories.Inventory;

// Closed-set whitelist mapping wire SortBy keys → SQL columns. Same
// SQL-injection-defence pattern as PurchaseOrderSortMapper. Unknown
// keys fall through to a.RequestedAt DESC.
public static class AdjustmentSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "adjustmentnumber" => "a.AdjustmentNumber",
            "product"          => "p.Code",
            "warehouse"        => "wh.Code",
            "location"         => "loc.Code",
            "owner"            => "ow.Code",
            "delta"            => "a.QuantityDelta",
            "reason"           => "a.Reason",
            "status"           => "a.Status",
            "requestedby"      => "u.FullName",
            "requestedat"      => "a.RequestedAt",
            _                  => "a.RequestedAt",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}
