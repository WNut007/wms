using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

// Phase 14E — ShipmentStatusMapper Theory coverage. 3 states (mirrors
// 14D Pack).
public class ShipmentStatusMapperTests
{
    [Theory]
    [InlineData("Pending",   "pending")]
    [InlineData("Shipped",   "shipped")]
    [InlineData("Cancelled", "cancelled")]
    public void ToWire_KnownDbValue_MapsToWire(string db, string expected) =>
        Assert.Equal(expected, ShipmentStatusMapper.ToWire(db));

    [Fact]
    public void ToWire_UnknownDbValue_FallsBackToPending() =>
        Assert.Equal("pending", ShipmentStatusMapper.ToWire("Mystery"));

    [Theory]
    [InlineData("pending",   "Pending")]
    [InlineData("shipped",   "Shipped")]
    [InlineData("cancelled", "Cancelled")]
    [InlineData("SHIPPED",   "Shipped")]    // case-insensitive
    public void FromWire_KnownValue_MapsToDb(string wire, string expected) =>
        Assert.Equal(expected, ShipmentStatusMapper.FromWire(wire));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("hax--")]
    public void FromWire_NullOrUnknown_ReturnsNull(string? wire) =>
        Assert.Null(ShipmentStatusMapper.FromWire(wire));

    [Theory]
    [InlineData("Pending")]
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public void RoundTrip_DbToWireToDb_Preserves(string db) =>
        Assert.Equal(db, ShipmentStatusMapper.FromWire(ShipmentStatusMapper.ToWire(db)));

    [Theory]
    [InlineData("Pending",   "neutral")]
    [InlineData("Shipped",   "success")]
    [InlineData("Cancelled", "neutral")]
    public void ToBadgeVariant_KnownDbValue(string db, string expected) =>
        Assert.Equal(expected, ShipmentStatusMapper.ToBadgeVariant(db));
}
