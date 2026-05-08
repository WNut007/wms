using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Mappers;

public class CategoryIconResolverTests
{
    [Theory]
    [InlineData("DEMO",        "ti-package",       "#534AB7")]
    [InlineData("ELECTRONICS", "ti-device-laptop", "#534AB7")]
    [InlineData("APPAREL",     "ti-shirt",         "#0C447C")]
    [InlineData("FOOD",        "ti-cookie",        "#854F0B")]
    [InlineData("BEAUTY",      "ti-sparkles",      "#A32D2D")]
    [InlineData("HOME",        "ti-armchair",      "#27500A")]
    [InlineData("TOYS",        "ti-puzzle",        "#BA7517")]
    public void Resolve_KnownCode_ReturnsMappedPair(string code, string icon, string color)
    {
        var (i, c) = CategoryIconResolver.Resolve(code);
        Assert.Equal(icon, i);
        Assert.Equal(color, c);
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("Demo")]
    [InlineData("dEmO")]
    public void Resolve_IsCaseInsensitive(string code)
    {
        var (i, c) = CategoryIconResolver.Resolve(code);
        Assert.Equal("ti-package", i);
        Assert.Equal("#534AB7", c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    [InlineData("Mystery")]
    public void Resolve_UnknownOrNull_FallsBackToBox(string? code)
    {
        var (i, c) = CategoryIconResolver.Resolve(code);
        Assert.Equal("ti-box", i);
        Assert.Equal("#888780", c);
    }
}
