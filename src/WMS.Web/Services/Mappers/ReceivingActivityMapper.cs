using WMS.DAL.Repositories.Inbound;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Services.Mappers;

// Maps an inbound.ReceivingHeaders row to the _ActivityPanel
// renderer's ActivityItem shape — the receipt-header side of the
// Warehouse Detail activity feed (Phase 6E).
//
// Visually distinct from MovementActivityMapper outputs so users can
// tell a "paperwork posted" event apart from the resulting per-stock
// movements: ti-truck-delivery icon (vs movement's ti-package-import)
// and a deeper green (#085041 vs movement's #639922). They sit
// naturally adjacent in the timeline — the receipt header arrives
// at the same timestamp as the receive movements it triggered, so
// the icons need to read as related-but-different.
//
// Free-text fields (PerformedByName, ReceivingNumber) are HTML-
// encoded — the renderer uses @Html.Raw for the title.
public static class ReceivingActivityMapper
{
    public static ActivityItem Map(ReceivingActivityRow r)
    {
        var (verb, color) = ResolveStatus(r.Status);

        var who = $"<span style=\"font-weight:500\">{System.Net.WebUtility.HtmlEncode(r.PerformedByName)}</span>";
        var rcvNum = System.Net.WebUtility.HtmlEncode(r.ReceivingNumber);
        var title = $"{who} {verb} receipt {rcvNum}";

        var description = r.LineCount switch
        {
            0 => string.Empty,
            1 => "1 line",
            _ => $"{r.LineCount} lines",
        };

        return new ActivityItem(
            Title:             title,
            Description:       description,
            IconClass:         "ti-truck-delivery",
            IconColor:         color,
            Timestamp:         r.ReceivedAt,
            TimestampRelative: RelativeTime.Format(r.ReceivedAt),
            // Reuse the movement mapper's date-bucket helper — same
            // UTC calendar-day buckets so receipt + movement entries
            // group under the same headers in the timeline.
            DateGroup:         MovementActivityMapper.BucketDateGroup(r.ReceivedAt));
    }

    // Status values mirror CK_ReceivingHeaders_Status. Cancelled
    // gets a red icon — visible-but-muted; unknown values flow to
    // a neutral "recorded" verb so a future status doesn't crash
    // the panel.
    private static (string Verb, string Color) ResolveStatus(string status) => status switch
    {
        "Posted"    => ("posted",    "#085041"),  // deep green
        "Draft"     => ("drafted",   "#888780"),  // muted gray
        "Cancelled" => ("cancelled", "#A32D2D"),  // red
        _           => ("recorded",  "#085041"),
    };
}
