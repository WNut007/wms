using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Services.Mappers;

// Maps a resolved-shape StockMovementListRow to the ActivityItem the
// _ActivityPanel renderer expects. Title carries the actor + verb +
// quantity + location clause as a single sentence (HTML — actor name
// is wrapped in a font-weight span, then HTML-escaped); description
// carries the secondary metadata (ReferenceType, Notes).
//
// All free-text fields that land in the title are HTML-encoded
// because the renderer uses @Html.Raw — operator-supplied location
// codes and user names must not break out into the markup. The
// font-weight span is the only HTML the mapper emits.
public static class MovementActivityMapper
{
    public static ActivityItem Map(StockMovementListRow m)
    {
        var (titleHtml, icon, color) = BuildTitle(m);

        // Description: ReferenceType · Notes. Each segment optional;
        // empty when both NULL (panel still renders an empty <p>).
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(m.ReferenceType)) parts.Add(m.ReferenceType);
        if (!string.IsNullOrEmpty(m.Notes))         parts.Add(m.Notes);
        var description = string.Join(" · ", parts);

        return new ActivityItem(
            Title:             titleHtml,
            Description:       description,
            IconClass:         icon,
            IconColor:         color,
            Timestamp:         m.PerformedAt,
            TimestampRelative: RelativeTime.Format(m.PerformedAt),
            DateGroup:         BucketDateGroup(m.PerformedAt));
    }

    // Bucket a timestamp into one of four UI groups the _ActivityPanel
    // renders as section headers. Today / Yesterday are calendar-day
    // anchored (UTC); This week catches anything older than yesterday
    // and within 7 days; Older catches the rest.
    public static string BucketDateGroup(DateTime performedAtUtc)
    {
        var todayStartUtc = DateTime.UtcNow.Date;
        if (performedAtUtc >= todayStartUtc)              return "Today";
        if (performedAtUtc >= todayStartUtc.AddDays(-1))  return "Yesterday";
        if (performedAtUtc >= todayStartUtc.AddDays(-7))  return "This week";
        return "Older";
    }

    // Per-MovementType title sentence. Putaway and Adjust split by
    // sign so paired rows (source / destination) render distinctly.
    // Pick / Transfer / Return / Cycle don't have writers yet —
    // placeholders ship now so when they land they render correctly
    // without a mapper change.
    private static (string TitleHtml, string Icon, string Color) BuildTitle(StockMovementListRow m)
    {
        var who   = WrapActor(m.PerformedByName);
        var qty   = FormatQty(m.QuantityDelta);
        var unit  = Math.Abs(m.QuantityDelta) == 1m ? "unit" : "units";
        var at    = LocationClause(" at ",   m.ToLocationCode);
        var into  = LocationClause(" into ", m.ToLocationCode);
        var from  = LocationClause(" from ", m.FromLocationCode);
        var transferClause = m.FromLocationCode is null && m.ToLocationCode is null
            ? string.Empty
            : $"{from}{at}";

        return m.MovementType switch
        {
            StockMovementType.Receive  => ($"{who} received {qty} {unit}{at}",                "ti-package-import",   "#639922"),
            StockMovementType.Putaway when m.QuantityDelta >= 0
                                       => ($"{who} putaway {qty} {unit}{into}",               "ti-arrow-down-right", "#0C447C"),
            StockMovementType.Putaway  => ($"{who} moved {qty} {unit}{from}",                 "ti-arrow-up-right",   "#0C447C"),
            StockMovementType.Pick     => ($"{who} picked {qty} {unit}{from}",                "ti-package-export",   "#BA7517"),
            StockMovementType.Adjust when m.QuantityDelta >= 0
                                       => ($"{who} adjusted +{qty} {unit}",                   "ti-edit",             "#854F0B"),
            StockMovementType.Adjust   => ($"{who} adjusted -{qty} {unit}",                   "ti-edit",             "#854F0B"),
            StockMovementType.Transfer => ($"{who} transferred {qty} {unit}{transferClause}", "ti-arrow-right",      "#534AB7"),
            StockMovementType.Return   => ($"{who} returned {qty} {unit}",                    "ti-rotate-clockwise", "#A32D2D"),
            StockMovementType.Cycle    => ($"{who} counted {qty} {unit}",                     "ti-list-check",       "#3C3489"),
            _                          => ($"{who} stock movement of {qty} {unit}",           "ti-circle-dot",       "#888780"),
        };
    }

    private static string WrapActor(string performedByName) =>
        $"<span style=\"font-weight:500\">{System.Net.WebUtility.HtmlEncode(performedByName)}</span>";

    // Unsigned, no trailing zeros (1, 5, 1.5, 2.25). Verb conveys
    // direction — "received -5" would be confusing.
    private static string FormatQty(decimal qty) =>
        Math.Abs(qty).ToString("0.##");

    // " at WAREHOUSE-MAIN" / " from BIN-A1" / etc. Empty when the
    // location code is NULL (Receive without destination, system row
    // missing, etc.) — keeps the title grammatical.
    private static string LocationClause(string separator, string? code) =>
        string.IsNullOrEmpty(code)
            ? string.Empty
            : $"{separator}{System.Net.WebUtility.HtmlEncode(code)}";
}
