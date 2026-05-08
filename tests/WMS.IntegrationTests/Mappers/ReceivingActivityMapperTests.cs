using WMS.DAL.Repositories.Inbound;
using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

public class ReceivingActivityMapperTests
{
    private static ReceivingActivityRow Row(
        string number = "RC-2026-0142",
        DateTime? receivedAt = null,
        string status = "Posted",
        string user = "Maya Rodriguez",
        int lineCount = 5) =>
        new(
            Id:               Guid.NewGuid(),
            ReceivingNumber:  number,
            ReceivedAt:       receivedAt ?? DateTime.UtcNow.AddHours(-3),
            Status:           status,
            PerformedByName:  user,
            LineCount:        lineCount);

    [Fact]
    public void Map_Posted_TitleHasUserVerbAndNumber()
    {
        var item = ReceivingActivityMapper.Map(Row());

        Assert.Contains("Maya Rodriguez", item.Title);
        Assert.Contains("posted receipt", item.Title);
        Assert.Contains("RC-2026-0142", item.Title);
    }

    [Theory]
    [InlineData("Posted",    "posted",    "#085041")]
    [InlineData("Draft",     "drafted",   "#888780")]
    [InlineData("Cancelled", "cancelled", "#A32D2D")]
    public void Map_Status_PicksVerbAndColor(string status, string verb, string color)
    {
        var item = ReceivingActivityMapper.Map(Row(status: status));

        Assert.Contains($" {verb} receipt ", item.Title);
        Assert.Equal(color, item.IconColor);
    }

    [Fact]
    public void Map_UnknownStatus_FallsBackToRecorded()
    {
        // Defensive — keeps the panel from going blank if a future
        // status value lands in the DB before the mapper is updated.
        var item = ReceivingActivityMapper.Map(Row(status: "MysteryState"));

        Assert.Contains(" recorded receipt ", item.Title);
        Assert.Equal("#085041", item.IconColor);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 line")]
    [InlineData(2, "2 lines")]
    [InlineData(50, "50 lines")]
    public void Map_LineCount_PluralizesInDescription(int n, string expected)
    {
        var item = ReceivingActivityMapper.Map(Row(lineCount: n));
        Assert.Equal(expected, item.Description);
    }

    [Fact]
    public void Map_IconIsTruckDelivery_DistinctFromMovementReceive()
    {
        // Visually distinct from MovementActivityMapper's
        // ti-package-import on a Receive — receipt-header events sit
        // adjacent to per-stock movements in the timeline, so the
        // icons must not read as "the same thing repeated".
        var item = ReceivingActivityMapper.Map(Row());
        Assert.Equal("ti-truck-delivery", item.IconClass);
    }

    [Fact]
    public void Map_ActorName_IsHtmlEncoded()
    {
        var item = ReceivingActivityMapper.Map(
            Row(user: "Maya <script>alert('x')</script>"));

        Assert.Contains("Maya &lt;script&gt;", item.Title);
        Assert.DoesNotContain("<script>alert", item.Title);
    }

    [Fact]
    public void Map_ReceivingNumber_IsHtmlEncoded()
    {
        var item = ReceivingActivityMapper.Map(Row(number: "RC<x>"));

        Assert.Contains("RC&lt;x&gt;", item.Title);
        Assert.DoesNotContain("<x>", item.Title);
    }

    [Fact]
    public void Map_ActorWrappedInFontWeightSpan()
    {
        var item = ReceivingActivityMapper.Map(Row());
        Assert.Contains("<span style=\"font-weight:500\">Maya Rodriguez</span>", item.Title);
    }

    [Fact]
    public void Map_PreservesReceivedAt_AsTimestamp()
    {
        var when = DateTime.UtcNow.AddHours(-7);
        var item = ReceivingActivityMapper.Map(Row(receivedAt: when));

        Assert.Equal(when, item.Timestamp);
    }

    [Fact]
    public void Map_DateGroupBucketing_MatchesMovementMapper()
    {
        // Receipt headers and movements need to bucket identically so
        // they group under the same timeline section headers. The
        // mapper delegates to MovementActivityMapper.BucketDateGroup —
        // pin that delegation explicitly.
        var when = DateTime.UtcNow.AddHours(-1);
        var item = ReceivingActivityMapper.Map(Row(receivedAt: when));

        Assert.Equal("Today", item.DateGroup);
        Assert.Equal(MovementActivityMapper.BucketDateGroup(when), item.DateGroup);
    }
}
