using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

// Pure-function tests. Lives in IntegrationTests for the same TFM
// reason as the other mapper tests — WMS.Web is net8.0-windows.
public class MovementActivityMapperTests
{
    private static StockMovementListRow Row(
        StockMovementType type,
        decimal qty,
        string? from = null,
        string? to = null,
        string user = "Maya Rodriguez",
        string? refType = null,
        string? notes = null,
        DateTime? performedAt = null) =>
        new(
            Id:               Guid.NewGuid(),
            MovementType:     type,
            QuantityDelta:    qty,
            FromLocationCode: from,
            ToLocationCode:   to,
            ReferenceType:    refType,
            Notes:            notes,
            PerformedByName:  user,
            PerformedAt:      performedAt ?? DateTime.UtcNow.AddHours(-2));

    [Fact]
    public void Map_Receive_TitleHasUserVerbQtyAndDestinationLocation()
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 5, to: "WH-MAIN"));

        Assert.Contains("Maya Rodriguez", item.Title);
        Assert.Contains("received 5 units", item.Title);
        Assert.Contains(" at WH-MAIN", item.Title);
        Assert.Equal("ti-package-import", item.IconClass);
        Assert.Equal("#639922", item.IconColor);
    }

    [Fact]
    public void Map_Receive_NoToLocation_OmitsLocationClause()
    {
        // Defensive: if the LEFT JOIN didn't resolve a location,
        // title stays grammatical without an awkward " at ".
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 5, to: null));

        Assert.Contains("received 5 units", item.Title);
        Assert.DoesNotContain(" at ", item.Title);
    }

    [Fact]
    public void Map_PutawayPositive_VerbPutawayInto()
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Putaway, +5, from: "STAGE-01", to: "BIN-A1"));

        Assert.Contains("putaway 5 units", item.Title);
        Assert.Contains(" into BIN-A1", item.Title);
        // Source clause not rendered on the destination-side row —
        // splits the paired entry's information by sign so users
        // mentally pair adjacent rows.
        Assert.DoesNotContain(" from STAGE-01", item.Title);
        Assert.Equal("ti-arrow-down-right", item.IconClass);
    }

    [Fact]
    public void Map_PutawayNegative_VerbMovedFrom()
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Putaway, -5, from: "STAGE-01", to: "BIN-A1"));

        Assert.Contains("moved 5 units", item.Title);
        Assert.Contains(" from STAGE-01", item.Title);
        Assert.DoesNotContain(" into BIN-A1", item.Title);
        Assert.Equal("ti-arrow-up-right", item.IconClass);
    }

    [Fact]
    public void Map_Pick_VerbPickedFrom()
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Pick, -3, from: "BIN-A1"));

        Assert.Contains("picked 3 units", item.Title);
        Assert.Contains(" from BIN-A1", item.Title);
    }

    [Theory]
    [InlineData(+5, "adjusted +5 units")]
    [InlineData(-5, "adjusted -5 units")]
    public void Map_AdjustSignedDelta_TitleCarriesSign(decimal qty, string expected)
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Adjust, qty));

        Assert.Contains(expected, item.Title);
    }

    [Fact]
    public void Map_Transfer_ShowsFromAndToOnSameRow()
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Transfer, 2, from: "WH-A", to: "WH-B"));

        Assert.Contains("transferred 2 units", item.Title);
        Assert.Contains(" from WH-A", item.Title);
        Assert.Contains(" at WH-B", item.Title);    // helper reuses " at " for the to-clause
    }

    [Theory]
    [InlineData(+5, "5 units")]
    [InlineData(-5, "5 units")]
    [InlineData(+1, "1 unit")]      // singular
    [InlineData(-1, "1 unit")]
    [InlineData(+1.5, "1.5 units")] // fractional → plural
    [InlineData(-2.25, "2.25 units")]
    public void Map_QtyInTitle_IsUnsignedAndPluralCorrect(decimal qty, string expected)
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, qty));

        Assert.Contains(expected, item.Title);
        // Unsigned — verb conveys direction. The CSS attribute
        // "font-weight" inside the actor span has a hyphen, so a bare
        // DoesNotContain("-") would always trip; check only that the
        // qty itself isn't sign-prefixed.
        Assert.DoesNotContain($"-{expected}", item.Title);
    }

    [Fact]
    public void Map_ActorName_IsHtmlEncoded()
    {
        // Operator-supplied display names land in the HTML title via
        // @Html.Raw — must be encoded at the mapper boundary.
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 1, user: "Maya <script>alert('x')</script>"));

        Assert.Contains("Maya &lt;script&gt;", item.Title);
        Assert.DoesNotContain("<script>alert", item.Title);
    }

    [Fact]
    public void Map_LocationCode_IsHtmlEncoded()
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 1, to: "BIN-<x>"));

        Assert.Contains("BIN-&lt;x&gt;", item.Title);
        Assert.DoesNotContain("<x>", item.Title);
    }

    [Fact]
    public void Map_ActorWrappedInFontWeightSpan()
    {
        // The single bit of HTML the mapper does emit — the actor
        // name gets the bold-ish weight wrapper to match the rest of
        // the design language.
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 1));

        Assert.Contains("<span style=\"font-weight:500\">Maya Rodriguez</span>", item.Title);
    }

    [Fact]
    public void Map_DescriptionHasReferenceAndNotes()
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 5, refType: "ReceivingLine", notes: "Damaged carton"));

        Assert.Equal("ReceivingLine · Damaged carton", item.Description);
    }

    [Fact]
    public void Map_DescriptionEmpty_WhenNoRefOrNotes()
    {
        // Quantity is in the title now — description is purely
        // metadata. No ref + no notes → empty description.
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 5));

        Assert.Equal(string.Empty, item.Description);
    }

    [Fact]
    public void Map_DescriptionHasOnlyRef_WhenNotesNull()
    {
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 5, refType: "ReceivingLine"));

        Assert.Equal("ReceivingLine", item.Description);
    }

    [Theory]
    [InlineData(StockMovementType.Receive,  "ti-package-import",   "#639922")]
    [InlineData(StockMovementType.Pick,     "ti-package-export",   "#BA7517")]
    [InlineData(StockMovementType.Adjust,   "ti-edit",             "#854F0B")]
    [InlineData(StockMovementType.Transfer, "ti-arrow-right",      "#534AB7")]
    [InlineData(StockMovementType.Return,   "ti-rotate-clockwise", "#A32D2D")]
    [InlineData(StockMovementType.Cycle,    "ti-list-check",       "#3C3489")]
    public void Map_IconAndColor_PerMovementType(StockMovementType type, string icon, string color)
    {
        var item = MovementActivityMapper.Map(Row(type, 1));
        Assert.Equal(icon, item.IconClass);
        Assert.Equal(color, item.IconColor);
    }

    [Fact]
    public void Map_PreservesPerformedAt_AsTimestamp()
    {
        var when = DateTime.UtcNow.AddHours(-7);
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 5, performedAt: when));

        Assert.Equal(when, item.Timestamp);
    }

    [Fact]
    public void Map_SystemUser_RendersInTitleVerbatim()
    {
        // Repo COALESCE flows 'System' literal through PerformedByName
        // when m.PerformedBy is NULL. Mapper just renders it.
        var item = MovementActivityMapper.Map(
            Row(StockMovementType.Receive, 5, user: "System"));

        Assert.Contains("<span style=\"font-weight:500\">System</span>", item.Title);
    }

    [Fact]
    public void BucketDateGroup_Today_ForRecentTimestamps() =>
        Assert.Equal("Today", MovementActivityMapper.BucketDateGroup(DateTime.UtcNow.AddHours(-1)));

    [Fact]
    public void BucketDateGroup_Yesterday_ForPriorCalendarDay() =>
        Assert.Equal("Yesterday", MovementActivityMapper.BucketDateGroup(DateTime.UtcNow.AddHours(-25)));

    [Fact]
    public void BucketDateGroup_ThisWeek_ForRecentDays() =>
        Assert.Equal("This week", MovementActivityMapper.BucketDateGroup(DateTime.UtcNow.AddDays(-3)));

    [Fact]
    public void BucketDateGroup_Older_BeyondAWeek() =>
        Assert.Equal("Older", MovementActivityMapper.BucketDateGroup(DateTime.UtcNow.AddDays(-30)));

    [Fact]
    public void BucketDateGroup_BoundaryAt7Days_StaysInThisWeek()
    {
        var t = DateTime.UtcNow.Date.AddDays(-7).AddHours(1);
        Assert.Equal("This week", MovementActivityMapper.BucketDateGroup(t));
    }
}
