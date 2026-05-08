using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

public class WarehouseStatusMapperTests
{
    [Theory]
    [InlineData(true,  "active")]
    [InlineData(false, "inactive")]
    public void ToWire_BoolToString(bool isActive, string expected) =>
        Assert.Equal(expected, WarehouseStatusMapper.ToWire(isActive));

    [Theory]
    [InlineData("active",   true)]
    [InlineData("inactive", false)]
    [InlineData("ACTIVE",   true)]      // case-insensitive
    public void FromWire_KnownValue_ParsesToBool(string wire, bool expected) =>
        Assert.Equal(expected, WarehouseStatusMapper.FromWire(wire));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    // 'maintenance' is the mock UI's third state — schema bool-only,
    // dropped on the way in (TD-009).
    [InlineData("maintenance")]
    [InlineData("hax--")]
    public void FromWire_NullOrUnknownOrMaintenance_ReturnsNull(string? wire) =>
        Assert.Null(WarehouseStatusMapper.FromWire(wire));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_BoolToWireToBool_Preserves(bool b) =>
        Assert.Equal(b, WarehouseStatusMapper.FromWire(WarehouseStatusMapper.ToWire(b)));
}
