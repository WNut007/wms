using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

// Phase 14A baseline + Phase 14B (Allocating/Allocated) + Phase 14C
// (Picking/Picked/PartiallyPicked) + Phase 14D (Packed) state additions.
public class SalesOrderStatusMapperTests
{
    [Theory]
    [InlineData("Draft",           "draft")]
    [InlineData("Open",            "open")]
    [InlineData("Allocating",      "allocating")]
    [InlineData("Allocated",       "allocated")]
    [InlineData("Picking",         "picking")]
    [InlineData("Picked",          "picked")]
    [InlineData("PartiallyPicked", "partiallypicked")]
    [InlineData("Packed",          "packed")]
    [InlineData("Cancelled",       "cancelled")]
    public void ToWire_KnownDbValue_MapsToWire(string db, string expected) =>
        Assert.Equal(expected, SalesOrderStatusMapper.ToWire(db));

    [Theory]
    [InlineData("draft",           "Draft")]
    [InlineData("open",            "Open")]
    [InlineData("allocating",      "Allocating")]
    [InlineData("allocated",       "Allocated")]
    [InlineData("picking",         "Picking")]
    [InlineData("picked",          "Picked")]
    [InlineData("partiallypicked", "PartiallyPicked")]
    [InlineData("packed",          "Packed")]
    [InlineData("cancelled",       "Cancelled")]
    [InlineData("PICKED",          "Picked")]      // case-insensitive
    [InlineData("PACKED",          "Packed")]
    public void FromWire_KnownValue_MapsToDb(string wire, string expected) =>
        Assert.Equal(expected, SalesOrderStatusMapper.FromWire(wire));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("hax--")]
    public void FromWire_NullOrUnknown_ReturnsNull(string? wire) =>
        Assert.Null(SalesOrderStatusMapper.FromWire(wire));

    [Theory]
    [InlineData("Draft")]
    [InlineData("Open")]
    [InlineData("Allocating")]
    [InlineData("Allocated")]
    [InlineData("Picking")]
    [InlineData("Picked")]
    [InlineData("PartiallyPicked")]
    [InlineData("Packed")]
    [InlineData("Cancelled")]
    public void RoundTrip_DbToWireToDb_Preserves(string db) =>
        Assert.Equal(db, SalesOrderStatusMapper.FromWire(SalesOrderStatusMapper.ToWire(db)));

    [Theory]
    [InlineData("Picking",         "warning")]
    [InlineData("Picked",          "success")]
    [InlineData("PartiallyPicked", "warning")]
    [InlineData("Packed",          "info")]   // Phase 14D: pack submitted, ready for ship
    public void ToBadgeVariant_NewStates(string db, string expected) =>
        Assert.Equal(expected, SalesOrderStatusMapper.ToBadgeVariant(db));
}
