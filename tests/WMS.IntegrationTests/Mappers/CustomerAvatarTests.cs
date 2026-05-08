using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

public class CustomerAvatarTests
{
    [Theory]
    [InlineData("Ananya Srisuk",          "AS")]
    [InlineData("Bangkok Trading Co.",    "BT")]
    [InlineData("ananya srisuk",          "AS")]    // upper-cased
    [InlineData("Pim",                    "P")]     // single word
    [InlineData("  Maria   Santos  ",     "MS")]    // collapses whitespace
    public void Initials_ProducesUppercasedFirstChars(string name, string expected) =>
        Assert.Equal(expected, CustomerAvatar.Initials(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Initials_NullOrBlank_ReturnsEmDash(string? name) =>
        Assert.Equal("—", CustomerAvatar.Initials(name));

    [Fact]
    public void Color_IsDeterministicAcrossCalls()
    {
        // The whole point is that the same name always renders with
        // the same chip color, even after a process restart. .NET's
        // string.GetHashCode is randomised per-domain, so this
        // implementation must NOT use it.
        var first  = CustomerAvatar.Color("Ananya Srisuk");
        var second = CustomerAvatar.Color("Ananya Srisuk");
        Assert.Equal(first, second);
    }

    [Fact]
    public void Color_DifferentSeeds_LikelyDifferentColors()
    {
        // Probabilistic — palette has 8 entries, so 2 random seeds
        // collide ~12.5% of the time. Pick three names spelled out so
        // that hash distribution gives at least 2 distinct colors.
        var c1 = CustomerAvatar.Color("Ananya Srisuk");
        var c2 = CustomerAvatar.Color("Bangkok Trading");
        var c3 = CustomerAvatar.Color("TechCorp Solutions");
        var distinct = new HashSet<string> { c1, c2, c3 };
        Assert.True(distinct.Count >= 2,
            $"Expected at least 2 distinct colors across 3 seeds, got {distinct.Count}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Color_NullOrEmpty_ReturnsFirstPaletteEntry(string? seed) =>
        Assert.Equal("#534AB7", CustomerAvatar.Color(seed));

    [Fact]
    public void Color_ReturnsHexFromPalette()
    {
        var palette = new[]
        {
            "#534AB7", "#0C447C", "#27500A", "#854F0B",
            "#A32D2D", "#3C3489", "#085041", "#BA7517",
        };
        // 50 random seeds — every result must be one of the palette
        // entries.
        for (var i = 0; i < 50; i++)
        {
            var c = CustomerAvatar.Color($"seed-{i}");
            Assert.Contains(c, palette);
        }
    }
}
