namespace WMS.DAL.Repositories.Inbound;

// Closed-set whitelist mapping wire SortBy keys → SQL columns. Same
// SQL-injection-defence pattern as ProductSortMapper / CustomerSort-
// Mapper (Phase 6B). Unknown keys fall through to ExpectedDate ASC.
public static class PurchaseOrderSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "ponumber"      => "po.PoNumber",
            "owner"         => "ow.Code",
            "warehouse"     => "wh.Code",
            "expecteddate"  => "po.ExpectedDate",
            "status"        => "po.Status",
            "linecount"     => "agg.LineCount",
            "totalexpected" => "agg.TotalExpectedQty",
            "totalreceived" => "agg.TotalReceivedQty",
            "createdat"     => "po.CreatedAt",
            "updatedat"     => "po.UpdatedAt",
            _               => "po.ExpectedDate",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}
