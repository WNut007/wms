using WMS.BLL.Services.Security;

namespace WMS.UnitTests.Services.Security;

// Phase 25 — pure-function policy validator. Closed-set rules; Theory
// coverage on each failure path + a happy-path round-trip.
public class PasswordPolicyTests
{
    [Theory]
    [InlineData(null,                "Password is required.")]
    [InlineData("",                  "Password is required.")]
    [InlineData("    ",              "Password is required.")]
    [InlineData("Short1",            "Password must be at least 8 characters.")]
    [InlineData("nouppercase1",      "Password must contain at least one uppercase letter.")]
    [InlineData("NOLOWERCASE1",      "Password must contain at least one lowercase letter.")]
    [InlineData("NoDigitsHere",      "Password must contain at least one digit.")]
    public void Validate_KnownFailures_ReturnExpectedMessage(string? input, string expected)
    {
        Assert.Equal(expected, PasswordPolicy.Validate(input));
    }

    [Theory]
    [InlineData("Password1")]
    [InlineData("MyP@ssw0rd")]
    [InlineData("aA1xxxxx")]            // minimum: 8 chars + mixed + digit
    [InlineData("CompleX123Password")]
    public void Validate_HappyPath_ReturnsNull(string input)
    {
        Assert.Null(PasswordPolicy.Validate(input));
    }

    [Fact]
    public void ThrowIfInvalid_OnFailure_ThrowsWithMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => PasswordPolicy.ThrowIfInvalid("short"));
        Assert.Contains("at least 8 characters", ex.Message);
    }

    [Fact]
    public void ThrowIfInvalid_OnSuccess_DoesNotThrow()
    {
        PasswordPolicy.ThrowIfInvalid("ValidPass1");
    }
}
