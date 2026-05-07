namespace WMS.Web.Services;

internal static class RelativeTime
{
    public static string Format(DateTime dt)
    {
        var span = DateTime.UtcNow - dt;
        if (span.TotalMinutes < 1)  return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)   return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7)     return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30)    return $"{(int)(span.TotalDays / 7)}w ago";
        if (span.TotalDays < 365)   return $"{(int)(span.TotalDays / 30)}mo ago";
        return $"{(int)(span.TotalDays / 365)}y ago";
    }
}
