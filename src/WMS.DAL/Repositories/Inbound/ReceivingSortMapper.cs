namespace WMS.DAL.Repositories.Inbound;

// Closed-set whitelist mapping wire SortBy keys → SQL columns. Same
// pattern as Phase 9A PurchaseOrderSortMapper. Default order:
// ReceivedAt DESC (newest first — natural for an audit list).
public static class ReceivingSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "receivingnumber" => "h.ReceivingNumber",
            "ponumber"        => "po.PoNumber",
            "vendor"          => "ow.Code",
            "warehouse"       => "wh.Code",
            "receivedat"      => "h.ReceivedAt",
            "status"          => "h.Status",
            "linecount"       => "agg.LineCount",
            "totalreceived"   => "agg.TotalReceivedQty",
            "createdat"       => "h.CreatedAt",
            _                 => "h.ReceivedAt",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}
