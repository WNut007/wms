using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;
using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

// Pure-function tests. Lives in IntegrationTests for the same TFM
// reason as the other mapper tests — WMS.Web is net8.0-windows.
public class MovementActivityMapperTests
{
    private static StockMovement Mvmt(
        StockMovementType type,
        decimal qty,
        DateTime? performedAt = null,
        string? refType = null,
        string? notes = null) =>
        new()
        {
            Id            = Guid.NewGuid(),
            StockId       = Guid.NewGuid(),
            MovementType  = type,
            QuantityDelta = qty,
            UomId         = Guid.NewGuid(),
            OwnerId       = Guid.NewGuid(),
            ReferenceType = refType,
            Notes         = notes,
            PerformedAt   = performedAt ?? DateTime.UtcNow.AddHours(-2),
        };

    [Fact]
    public void Map_Receive_GreenPackageImportIcon()
    {
        var item = MovementActivityMapper.Map(Mvmt(StockMovementType.Receive, 5));

        Assert.Equal("Stock received", item.Title);
        Assert.Equal("ti-package-import", item.IconClass);
        Assert.Equal("#639922", item.IconColor);
    }

    [Fact]
    public void Map_PutawayPositive_RendersAsInbound()
    {
        // Destination row of a putaway pair — QuantityDelta > 0.
        var item = MovementActivityMapper.Map(Mvmt(StockMovementType.Putaway, +5));

        Assert.Equal("Putaway (in)", item.Title);
        Assert.Equal("ti-arrow-down-right", item.IconClass);
    }

    [Fact]
    public void Map_PutawayNegative_RendersAsOutbound()
    {
        // Source row of a putaway pair — QuantityDelta < 0.
        var item = MovementActivityMapper.Map(Mvmt(StockMovementType.Putaway, -5));

        Assert.Equal("Putaway (out)", item.Title);
        Assert.Equal("ti-arrow-up-right", item.IconClass);
    }

    [Theory]
    [InlineData(StockMovementType.Pick,     "Stock picked")]
    [InlineData(StockMovementType.Transfer, "Stock transferred")]
    [InlineData(StockMovementType.Return,   "Stock returned")]
    [InlineData(StockMovementType.Cycle,    "Cycle count")]
    public void Map_DeferredTypes_HavePlaceholderTitles(StockMovementType type, string expected)
    {
        // Pick / Transfer / Return / Cycle don't have writers yet —
        // the mapper still produces sane output so when the writers
        // land no mapper update is needed.
        var item = MovementActivityMapper.Map(Mvmt(type, 10));
        Assert.Equal(expected, item.Title);
    }

    [Theory]
    [InlineData(+5, "Stock adjusted (+)")]
    [InlineData(-5, "Stock adjusted (−)")]
    public void Map_AdjustSign_DistinguishesPosNeg(decimal qty, string expected)
    {
        var item = MovementActivityMapper.Map(Mvmt(StockMovementType.Adjust, qty));
        Assert.Equal(expected, item.Title);
    }

    [Theory]
    [InlineData(+5,    "+5 units")]
    [InlineData(-3,    "-3 units")]
    [InlineData(0,     "0 units")]
    [InlineData(+1.5,  "+1.5 units")]
    [InlineData(-2.25, "-2.25 units")]
    public void Map_DescriptionOpensWithSignedQty(decimal qty, string startsWith)
    {
        var item = MovementActivityMapper.Map(Mvmt(StockMovementType.Receive, qty));
        Assert.StartsWith(startsWith, item.Description);
    }

    [Fact]
    public void Map_DescriptionAppendsRefTypeAndNotes()
    {
        var item = MovementActivityMapper.Map(
            Mvmt(StockMovementType.Receive, 5, refType: "ReceivingLine", notes: "Damaged carton"));

        Assert.Equal("+5 units · ReceivingLine · Damaged carton", item.Description);
    }

    [Fact]
    public void Map_DescriptionOmitsEmptySegments()
    {
        // No ref, no notes — description is just the qty segment.
        var item = MovementActivityMapper.Map(Mvmt(StockMovementType.Receive, 5));
        Assert.Equal("+5 units", item.Description);
    }

    [Fact]
    public void Map_PreservesPerformedAt_AsTimestamp()
    {
        var when = DateTime.UtcNow.AddHours(-7);
        var item = MovementActivityMapper.Map(Mvmt(StockMovementType.Receive, 5, performedAt: when));

        Assert.Equal(when, item.Timestamp);
    }

    [Fact]
    public void BucketDateGroup_Today_ForRecentTimestamps()
    {
        // 1 hour ago is unambiguously "Today" no matter the local
        // time of day — buckets use UTC calendar dates.
        Assert.Equal("Today", MovementActivityMapper.BucketDateGroup(DateTime.UtcNow.AddHours(-1)));
    }

    [Fact]
    public void BucketDateGroup_Yesterday_ForPriorCalendarDay()
    {
        // 25 hours ago lands cleanly in yesterday's calendar day in
        // every test-run hour-of-day.
        Assert.Equal("Yesterday", MovementActivityMapper.BucketDateGroup(DateTime.UtcNow.AddHours(-25)));
    }

    [Fact]
    public void BucketDateGroup_ThisWeek_ForRecentDays()
    {
        Assert.Equal("This week", MovementActivityMapper.BucketDateGroup(DateTime.UtcNow.AddDays(-3)));
    }

    [Fact]
    public void BucketDateGroup_Older_BeyondAWeek()
    {
        Assert.Equal("Older", MovementActivityMapper.BucketDateGroup(DateTime.UtcNow.AddDays(-30)));
    }

    [Fact]
    public void BucketDateGroup_BoundaryAt7Days_StaysInThisWeek()
    {
        // Boundary check — 6 days 23 hours ago should still be
        // "This week", not "Older". Using AddDays(-7).AddHours(1)
        // keeps it just inside the window.
        var t = DateTime.UtcNow.Date.AddDays(-7).AddHours(1);
        Assert.Equal("This week", MovementActivityMapper.BucketDateGroup(t));
    }
}
