namespace WMS.DAL.Repositories.Inventory;

// Closed-set whitelist (SQL-injection defence). Unknown keys fall
// through to t.RequestedAt DESC.
public static class TransferOrderSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "transfernumber"  => "t.TransferNumber",
            "fromwarehouse"   => "fwh.Code",
            "towarehouse"     => "twh.Code",
            "status"          => "t.Status",
            "reason"          => "t.Reason",
            "linecount"       => "agg.LineCount",
            "requestedby"     => "u.FullName",
            "requestedat"     => "t.RequestedAt",
            _                 => "t.RequestedAt",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}
