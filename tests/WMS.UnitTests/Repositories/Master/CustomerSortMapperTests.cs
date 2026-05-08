using WMS.DAL.Repositories.Master;

namespace WMS.UnitTests.Repositories.Master;

public class CustomerSortMapperTests
{
    [Theory]
    [InlineData("code",    false, "Code ASC")]
    [InlineData("name",    false, "Name ASC")]
    [InlineData("type",    false, "CustomerType ASC")]
    [InlineData("country", false, "Country ASC")]
    public void KnownKeys_MapToWhitelistedColumns(string sortBy, bool desc, string expected) =>
        Assert.Equal(expected, CustomerSortMapper.ToOrderByClause(sortBy, desc));

    [Theory]
    [InlineData("Code",    "Code ASC")]
    [InlineData("CODE",    "Code ASC")]
    [InlineData("Type",    "CustomerType ASC")]
    [InlineData("COUNTRY", "Country ASC")]
    public void Match_IsCaseInsensitive(string sortBy, string expected) =>
        Assert.Equal(expected, CustomerSortMapper.ToOrderByClause(sortBy, desc: false));

    [Theory]
    [InlineData("name", "Name DESC")]
    [InlineData("code", "Code DESC")]
    public void DescFlag_AppendsDESC(string sortBy, string expected) =>
        Assert.Equal(expected, CustomerSortMapper.ToOrderByClause(sortBy, desc: true));

    [Theory]
    // Mock UI exposes these sort options but there's no orders schema
    // yet (TD-011) — they must NOT reach the SQL ORDER BY. Closed-set
    // whitelist drops them silently.
    [InlineData("totalorders")]
    [InlineData("lastorderdate")]
    public void OrderRelatedKeys_FallThrough_UntilTD011(string sortBy) =>
        Assert.Equal("Name ASC", CustomerSortMapper.ToOrderByClause(sortBy, desc: false));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hax--")]
    [InlineData("'; DROP TABLE Customers; --")]
    [InlineData("revenue")]    // not in mock either
    public void UnknownOrEmpty_FallsBackToNameAsc(string? sortBy) =>
        Assert.Equal("Name ASC", CustomerSortMapper.ToOrderByClause(sortBy, desc: false));

    [Fact]
    public void Default_NullKeyDescFalse_IsNameAsc() =>
        Assert.Equal("Name ASC", CustomerSortMapper.ToOrderByClause(null, desc: false));
}
