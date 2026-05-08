using WMS.DAL.Repositories.Master;

namespace WMS.UnitTests.Repositories.Master;

public class WarehouseSortMapperTests
{
    [Theory]
    [InlineData("code",      false, "w.Code ASC")]
    [InlineData("name",      false, "w.Name ASC")]
    [InlineData("type",      false, "w.Type ASC")]
    [InlineData("locations", false, "LocationCount ASC")]
    [InlineData("updatedat", false, "w.UpdatedAt ASC")]
    public void KnownKeys_MapToWhitelistedColumns(string sortBy, bool desc, string expected) =>
        Assert.Equal(expected, WarehouseSortMapper.ToOrderByClause(sortBy, desc));

    [Theory]
    [InlineData("Code",      "w.Code ASC")]
    [InlineData("CODE",      "w.Code ASC")]
    [InlineData("Locations", "LocationCount ASC")]
    [InlineData("UPDATEDAT", "w.UpdatedAt ASC")]
    public void Match_IsCaseInsensitive(string sortBy, string expected) =>
        Assert.Equal(expected, WarehouseSortMapper.ToOrderByClause(sortBy, desc: false));

    [Theory]
    [InlineData("name",      "w.Name DESC")]
    [InlineData("locations", "LocationCount DESC")]
    public void DescFlag_AppendsDESC(string sortBy, string expected) =>
        Assert.Equal(expected, WarehouseSortMapper.ToOrderByClause(sortBy, desc: true));

    [Fact]
    // Mock UI exposed a Region facet not backed by any schema column —
    // Phase 6B drops it (brief §4 sub-decisions). Frontend dropdowns
    // may still emit sortBy=region; the whitelist must absorb it.
    public void Region_FallsThroughToNameAsc() =>
        Assert.Equal("w.Name ASC", WarehouseSortMapper.ToOrderByClause("region", desc: false));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hax--")]
    [InlineData("'; DROP TABLE Warehouses; --")]
    [InlineData("subtitle")]    // also dropped from schema/UI
    public void UnknownOrEmpty_FallsBackToNameAsc(string? sortBy) =>
        Assert.Equal("w.Name ASC", WarehouseSortMapper.ToOrderByClause(sortBy, desc: false));

    [Fact]
    public void Default_NullKeyDescFalse_IsNameAsc() =>
        Assert.Equal("w.Name ASC", WarehouseSortMapper.ToOrderByClause(null, desc: false));
}
