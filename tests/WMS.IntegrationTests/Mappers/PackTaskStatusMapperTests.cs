using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

// Phase 14D — PackTaskStatusMapper Theory coverage. 3 states (simpler
// than Pick's 5 because pack workflow is single-shot for MVP).
public class PackTaskStatusMapperTests
{
    [Theory]
    [InlineData("Pending",   "pending")]
    [InlineData("Packed",    "packed")]
    [InlineData("Cancelled", "cancelled")]
    public void ToWire_KnownDbValue_MapsToWire(string db, string expected) =>
        Assert.Equal(expected, PackTaskStatusMapper.ToWire(db));

    [Fact]
    public void ToWire_UnknownDbValue_FallsBackToPending() =>
        Assert.Equal("pending", PackTaskStatusMapper.ToWire("Mystery"));

    [Theory]
    [InlineData("pending",   "Pending")]
    [InlineData("packed",    "Packed")]
    [InlineData("cancelled", "Cancelled")]
    [InlineData("PACKED",    "Packed")]    // case-insensitive
    public void FromWire_KnownValue_MapsToDb(string wire, string expected) =>
        Assert.Equal(expected, PackTaskStatusMapper.FromWire(wire));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("hax--")]
    public void FromWire_NullOrUnknown_ReturnsNull(string? wire) =>
        Assert.Null(PackTaskStatusMapper.FromWire(wire));

    [Theory]
    [InlineData("Pending")]
    [InlineData("Packed")]
    [InlineData("Cancelled")]
    public void RoundTrip_DbToWireToDb_Preserves(string db) =>
        Assert.Equal(db, PackTaskStatusMapper.FromWire(PackTaskStatusMapper.ToWire(db)));

    [Theory]
    [InlineData("Pending",   "neutral")]
    [InlineData("Packed",    "success")]
    [InlineData("Cancelled", "neutral")]
    public void ToBadgeVariant_KnownDbValue(string db, string expected) =>
        Assert.Equal(expected, PackTaskStatusMapper.ToBadgeVariant(db));
}
