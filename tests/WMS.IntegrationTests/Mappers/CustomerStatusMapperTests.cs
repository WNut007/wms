using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

public class CustomerStatusMapperTests
{
    [Theory]
    [InlineData("Active",    "active")]
    [InlineData("Inactive",  "inactive")]
    [InlineData("Suspended", "suspended")]
    [InlineData("Draft",     "pending")]    // mock vocabulary: 'pending' = Draft
    public void ToWire_KnownDbValue_MapsToWire(string db, string expected) =>
        Assert.Equal(expected, CustomerStatusMapper.ToWire(db));

    [Fact]
    public void ToWire_UnknownDbValue_FallsBackToActive() =>
        Assert.Equal("active", CustomerStatusMapper.ToWire("MysteryState"));

    [Theory]
    [InlineData("active",    "Active")]
    [InlineData("inactive",  "Inactive")]
    [InlineData("suspended", "Suspended")]
    [InlineData("pending",   "Draft")]      // mock vocabulary
    [InlineData("draft",     "Draft")]      // forward-compat alias
    [InlineData("PENDING",   "Draft")]      // case-insensitive
    public void FromWire_KnownValue_MapsToDb(string wire, string expected) =>
        Assert.Equal(expected, CustomerStatusMapper.FromWire(wire));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("hax--")]
    public void FromWire_NullOrUnknown_ReturnsNull(string? wire) =>
        Assert.Null(CustomerStatusMapper.FromWire(wire));

    [Theory]
    [InlineData("Active")]
    [InlineData("Inactive")]
    [InlineData("Suspended")]
    [InlineData("Draft")]
    public void RoundTrip_DbToWireToDb_Preserves(string db) =>
        Assert.Equal(db, CustomerStatusMapper.FromWire(CustomerStatusMapper.ToWire(db)));
}
