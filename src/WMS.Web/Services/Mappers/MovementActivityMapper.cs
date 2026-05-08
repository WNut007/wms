using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Services.Mappers;

// Maps an immutable inventory.StockMovements row to the existing
// _ActivityPanel renderer's ActivityItem shape. Per-MovementType
// formatting picks an icon + color per the design palette; signed
// QuantityDelta drives the in/out distinction for Putaway and Adjust.
//
// Phase 6C scope: title text is movement-type only — no user-name or
// location-code resolution yet (would need batch lookups against
// security.Users + master.Locations; logged as TD-014). Reference
// codes (`ReceivingLine`, etc.) and Notes appear in the description
// when present.
public static class MovementActivityMapper
{
    public static ActivityItem Map(StockMovement m)
    {
        var (title, icon, color) = Resolve(m);

        // Sign-formatted delta — "+5", "-3", "0". The +/- on the
        // wire makes the direction unambiguous when 2 putaway rows
        // appear next to each other (source -, destination +).
        var qty = m.QuantityDelta.ToString("+#,##0.##;-#,##0.##;0");

        // Description: signed qty · ref-type · notes. Each segment
        // appears only if it has content; drops to "—" if all empty.
        var parts = new List<string> { $"{qty} units" };
        if (!string.IsNullOrEmpty(m.ReferenceType)) parts.Add(m.ReferenceType);
        if (!string.IsNullOrEmpty(m.Notes))         parts.Add(m.Notes);
        var description = string.Join(" · ", parts);

        return new ActivityItem(
            Title:             title,
            Description:       description,
            IconClass:         icon,
            IconColor:         color,
            Timestamp:         m.PerformedAt,
            TimestampRelative: RelativeTime.Format(m.PerformedAt),
            DateGroup:         BucketDateGroup(m.PerformedAt));
    }

    // Bucket a timestamp into one of four UI groups the
    // _ActivityPanel renders as section headers. Today / Yesterday
    // are calendar-day anchored (UTC); This week catches anything
    // older than yesterday and within 7 days; Older catches the
    // rest. Same scheme as the Phase 4 mock.
    public static string BucketDateGroup(DateTime performedAtUtc)
    {
        var todayStartUtc = DateTime.UtcNow.Date;
        if (performedAtUtc >= todayStartUtc)              return "Today";
        if (performedAtUtc >= todayStartUtc.AddDays(-1))  return "Yesterday";
        if (performedAtUtc >= todayStartUtc.AddDays(-7))  return "This week";
        return "Older";
    }

    // Title + icon + color per MovementType. Putaway and Adjust
    // split into "in"/"out" variants by sign so paired rows render
    // distinctly. Pick / Transfer / Return / Cycle don't have
    // implementations yet — placeholders ship now so when they
    // land they render correctly without a mapper change.
    private static (string Title, string Icon, string Color) Resolve(StockMovement m) =>
        m.MovementType switch
        {
            StockMovementType.Receive  => ("Stock received",      "ti-package-import",   "#639922"),  // green
            StockMovementType.Putaway when m.QuantityDelta >= 0
                                       => ("Putaway (in)",        "ti-arrow-down-right", "#0C447C"),  // blue
            StockMovementType.Putaway  => ("Putaway (out)",       "ti-arrow-up-right",   "#0C447C"),  // blue (source)
            StockMovementType.Pick     => ("Stock picked",        "ti-package-export",   "#BA7517"),  // amber
            StockMovementType.Adjust when m.QuantityDelta >= 0
                                       => ("Stock adjusted (+)",  "ti-edit",             "#854F0B"),  // amber
            StockMovementType.Adjust   => ("Stock adjusted (−)",  "ti-edit",             "#854F0B"),
            StockMovementType.Transfer => ("Stock transferred",   "ti-arrow-right",      "#534AB7"),  // purple
            StockMovementType.Return   => ("Stock returned",      "ti-rotate-clockwise", "#A32D2D"),  // red
            StockMovementType.Cycle    => ("Cycle count",         "ti-list-check",       "#3C3489"),
            _                          => ("Stock movement",      "ti-circle-dot",       "#888780"),
        };
}
