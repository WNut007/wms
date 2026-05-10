namespace WMS.DAL.Repositories.Outbound;

// Phase 15A — closed-set whitelist mapping wire SortBy keys → SQL
// columns. SQL-injection defence; unknown keys fall through to the
// default. Mirrors Phase 14A SalesOrderSortMapper pattern.
public static class PickTaskSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "picknumber"   => "pt.PickNumber",
            "sonumber"     => "so.SoNumber",
            "customer"     => "c.Code",
            "status"       => "pt.Status",
            "linecount"    => "agg.LineCount",
            "generatedat"  => "pt.GeneratedAt",
            "completedat"  => "pt.CompletedAt",
            "cancelledat"  => "pt.CancelledAt",
            _              => "pt.GeneratedAt",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}
