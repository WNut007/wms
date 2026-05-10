namespace WMS.DAL.Repositories.Outbound;

// Phase 15A — closed-set whitelist mapping wire SortBy keys → SQL
// columns. SQL-injection defence; mirrors PickTaskSortMapper.
public static class PackTaskSortMapper
{
    public static string ToOrderByClause(string? sortBy, bool desc)
    {
        var col = (sortBy ?? "").ToLowerInvariant() switch
        {
            "packnumber"   => "pt.PackNumber",
            "sonumber"     => "so.SoNumber",
            "customer"     => "c.Code",
            "status"       => "pt.Status",
            "linecount"    => "agg.LineCount",
            "generatedat"  => "pt.GeneratedAt",
            "packedat"     => "pt.PackedAt",
            "cancelledat"  => "pt.CancelledAt",
            _              => "pt.GeneratedAt",
        };
        return desc ? $"{col} DESC" : $"{col} ASC";
    }
}
