namespace WMS.DAL.Repositories.Outbound;

// Phase 14A — closed-set whitelist mapping wire SortBy keys → SQL
// columns. SQL-injection defence; unknown keys fall through to the
// default. Mirrors the Phase 9A PurchaseOrderSortMapper / Phase 13
// TransferOrderSortMapper pattern.
public static class SalesOrderSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "sonumber"          => "so.SoNumber",
            "customer"          => "c.Code",
            "warehouse"         => "wh.Code",
            "orderdate"         => "so.OrderDate",
            "requestedshipdate" => "so.RequestedShipDate",
            "status"            => "so.Status",
            "linecount"         => "agg.LineCount",
            "totalquantity"     => "agg.TotalQuantity",
            "createdat"         => "so.CreatedAt",
            "updatedat"         => "so.UpdatedAt",
            _                   => "so.OrderDate",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}
