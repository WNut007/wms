namespace WMS.Web.ViewModels.Reports;

// Phase 23 — fixed date-range presets for Orders + KPIs reports.
// Custom ranges = future TD. Resolves a preset name to a (fromUtc,
// toUtc) tuple at request time. Bounds are half-open: [from, to).
//
// All bounds anchored to UTC "now" to keep the report deterministic
// across timezones (tenants can be globally distributed). Display in
// the view uses the same UTC anchor so labels reconcile.
public static class DateRangePreset
{
    public const string Today    = "today";
    public const string Week     = "week";     // last 7 days
    public const string Month    = "month";    // last 30 days
    public const string Quarter  = "quarter";  // last 90 days
    public const string Year     = "year";     // last 365 days

    public const string Default = Month;

    public static (DateTime FromUtc, DateTime ToUtc, string Label) Resolve(string? preset)
    {
        var now = DateTime.UtcNow;
        return preset switch
        {
            Today   => (now.Date,                now,             "Today"),
            Week    => (now.AddDays(-7),         now,             "Last 7 days"),
            Quarter => (now.AddDays(-90),        now,             "Last 90 days"),
            Year    => (now.AddDays(-365),       now,             "Last year"),
            _       => (now.AddDays(-30),        now,             "Last 30 days"),
        };
    }

    public static string NormalisePreset(string? preset) =>
        preset switch
        {
            Today or Week or Month or Quarter or Year => preset!,
            _ => Default,
        };
}
