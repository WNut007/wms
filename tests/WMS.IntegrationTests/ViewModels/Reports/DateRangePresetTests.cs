using WMS.Web.ViewModels.Reports;

namespace WMS.IntegrationTests.ViewModels.Reports;

// Phase 23 — pure-function preset resolver. Lightweight Theory
// coverage on the closed-set name → range mapping + the
// NormalisePreset fallback.
public class DateRangePresetTests
{
    [Theory]
    [InlineData("today",    "Today")]
    [InlineData("week",     "Last 7 days")]
    [InlineData("month",    "Last 30 days")]
    [InlineData("quarter",  "Last 90 days")]
    [InlineData("year",     "Last year")]
    [InlineData(null,       "Last 30 days")]  // default
    [InlineData("bogus",    "Last 30 days")]  // unknown → default
    [InlineData("",         "Last 30 days")]
    public void Resolve_KnownPresets_ProduceExpectedLabel(string? preset, string expectedLabel)
    {
        var (_, _, label) = DateRangePreset.Resolve(preset);
        Assert.Equal(expectedLabel, label);
    }

    [Fact]
    public void Resolve_HalfOpenRange_FromBeforeTo()
    {
        var (from, to, _) = DateRangePreset.Resolve("week");
        Assert.True(from < to);
        Assert.InRange((to - from).Days, 6, 7);  // small clock drift tolerated
    }

    [Theory]
    [InlineData("today",   "today")]
    [InlineData("week",    "week")]
    [InlineData("month",   "month")]
    [InlineData("quarter", "quarter")]
    [InlineData("year",    "year")]
    [InlineData(null,      "month")]
    [InlineData("",        "month")]
    [InlineData("garbage", "month")]
    public void NormalisePreset_ReturnsClosedSetOrDefault(string? input, string expected)
    {
        Assert.Equal(expected, DateRangePreset.NormalisePreset(input));
    }
}
