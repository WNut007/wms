using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

// Phase 14C — PickTaskStatusMapper Theory coverage. Same shape as the
// other status-mapper test fixtures (Customer / SalesOrder / etc).
public class PickTaskStatusMapperTests
{
    [Theory]
    [InlineData("Pending",         "pending")]
    [InlineData("InProgress",      "inprogress")]
    [InlineData("Picked",          "picked")]
    [InlineData("PartiallyPicked", "partiallypicked")]
    [InlineData("Cancelled",       "cancelled")]
    public void ToWire_KnownDbValue_MapsToWire(string db, string expected) =>
        Assert.Equal(expected, PickTaskStatusMapper.ToWire(db));

    [Fact]
    public void ToWire_UnknownDbValue_FallsBackToPending() =>
        Assert.Equal("pending", PickTaskStatusMapper.ToWire("Mystery"));

    [Theory]
    [InlineData("pending",          "Pending")]
    [InlineData("inprogress",       "InProgress")]
    [InlineData("picked",           "Picked")]
    [InlineData("partiallypicked",  "PartiallyPicked")]
    [InlineData("cancelled",        "Cancelled")]
    [InlineData("PENDING",          "Pending")]    // case-insensitive
    public void FromWire_KnownValue_MapsToDb(string wire, string expected) =>
        Assert.Equal(expected, PickTaskStatusMapper.FromWire(wire));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("hax--")]
    public void FromWire_NullOrUnknown_ReturnsNull(string? wire) =>
        Assert.Null(PickTaskStatusMapper.FromWire(wire));

    [Theory]
    [InlineData("Pending")]
    [InlineData("InProgress")]
    [InlineData("Picked")]
    [InlineData("PartiallyPicked")]
    [InlineData("Cancelled")]
    public void RoundTrip_DbToWireToDb_Preserves(string db) =>
        Assert.Equal(db, PickTaskStatusMapper.FromWire(PickTaskStatusMapper.ToWire(db)));

    [Theory]
    [InlineData("Pending",         "neutral")]
    [InlineData("InProgress",      "warning")]
    [InlineData("Picked",          "success")]
    [InlineData("PartiallyPicked", "warning")]
    [InlineData("Cancelled",       "neutral")]
    public void ToBadgeVariant_KnownDbValue(string db, string expected) =>
        Assert.Equal(expected, PickTaskStatusMapper.ToBadgeVariant(db));
}
