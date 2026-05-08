using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

// Pure-function helper test — lives here (rather than WMS.UnitTests)
// because WMS.Web targets net8.0-windows and WMS.UnitTests is net8.0.
// Same pattern as RequirePermissionAttributeTests in this project.
public class ProductStatusMapperTests
{
    [Theory]
    [InlineData("Active",       "active")]
    [InlineData("Inactive",     "inactive")]
    [InlineData("Discontinued", "discontinued")]
    [InlineData("Draft",        "draft")]
    public void ToWire_KnownDbValue_LowercasesIt(string db, string expected) =>
        Assert.Equal(expected, ProductStatusMapper.ToWire(db));

    [Fact]
    public void ToWire_UnknownDbValue_FallsBackToActive() =>
        // Defensive default — never hit if the DB CHECK is enforced.
        Assert.Equal("active", ProductStatusMapper.ToWire("MysteryState"));

    [Theory]
    [InlineData("active",       "Active")]
    [InlineData("inactive",     "Inactive")]
    [InlineData("discontinued", "Discontinued")]
    [InlineData("draft",        "Draft")]
    [InlineData("ACTIVE",       "Active")]      // case-insensitive
    [InlineData("Active",       "Active")]
    public void FromWire_KnownValue_PascalCasesIt(string wire, string expected) =>
        Assert.Equal(expected, ProductStatusMapper.FromWire(wire));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData("out_of_stock")]   // mock-only, not a real status
    [InlineData("hax--")]
    public void FromWire_NullOrUnknown_ReturnsNull(string? wire) =>
        Assert.Null(ProductStatusMapper.FromWire(wire));

    [Theory]
    [InlineData("Active")]
    [InlineData("Inactive")]
    [InlineData("Discontinued")]
    [InlineData("Draft")]
    public void RoundTrip_DbToWireToDb_Preserves(string db) =>
        Assert.Equal(db, ProductStatusMapper.FromWire(ProductStatusMapper.ToWire(db)));
}
