using WMS.DAL.Repositories.Master;

namespace WMS.UnitTests.Repositories.Master;

public class ProductSortMapperTests
{
    [Theory]
    [InlineData("code",      false, "p.Code ASC")]
    [InlineData("name",      false, "p.Name ASC")]
    [InlineData("brand",     false, "p.Brand ASC")]
    [InlineData("category",  false, "c.Code ASC")]
    [InlineData("stock",     false, "StockOnHand ASC")]
    [InlineData("updatedat", false, "p.UpdatedAt ASC")]
    public void KnownKeys_MapToWhitelistedColumns(string sortBy, bool desc, string expected) =>
        Assert.Equal(expected, ProductSortMapper.ToOrderByClause(sortBy, desc));

    [Theory]
    [InlineData("Code",      "p.Code ASC")]
    [InlineData("CODE",      "p.Code ASC")]
    [InlineData("UpdatedAt", "p.UpdatedAt ASC")]
    [InlineData("UPDATEDAT", "p.UpdatedAt ASC")]
    public void Match_IsCaseInsensitive(string sortBy, string expected) =>
        Assert.Equal(expected, ProductSortMapper.ToOrderByClause(sortBy, desc: false));

    [Theory]
    [InlineData("name", "p.Name DESC")]
    [InlineData("code", "p.Code DESC")]
    public void DescFlag_AppendsDESC(string sortBy, string expected) =>
        Assert.Equal(expected, ProductSortMapper.ToOrderByClause(sortBy, desc: true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hax--")]
    [InlineData("'; DROP TABLE Products; --")]
    [InlineData("price")]    // not in mock either, defensively rejected
    public void UnknownOrEmpty_FallsBackToNameAsc(string? sortBy) =>
        // Whitespace passes through TryGetValue with no match (the
        // dictionary has no whitespace keys) → falls back. Hostile
        // strings can never reach the ORDER BY clause as-is.
        Assert.Equal("p.Name ASC", ProductSortMapper.ToOrderByClause(sortBy, desc: false));

    [Fact]
    public void Default_NullKeyDescFalse_IsNameAsc() =>
        Assert.Equal("p.Name ASC", ProductSortMapper.ToOrderByClause(null, desc: false));
}
