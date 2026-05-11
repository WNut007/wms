using Microsoft.Extensions.Caching.Memory;
using WMS.BLL.Services.Auth;

namespace WMS.UnitTests.Services.Auth;

// Phase 25 — per-IP throttle. Configured to 3 attempts per 1-minute
// window in tests for fast assertions.
public class LoginRateLimiterTests
{
    private static LoginRateLimiter NewLimiter(int max = 3, double seconds = 60) =>
        new(new MemoryCache(new MemoryCacheOptions()),
            maxAttemptsPerWindow: max,
            window: TimeSpan.FromSeconds(seconds));

    [Fact]
    public void TryRegisterAttempt_UpToThreshold_AllAllowed()
    {
        var sut = NewLimiter(max: 3);
        Assert.True(sut.TryRegisterAttempt("1.2.3.4"));
        Assert.True(sut.TryRegisterAttempt("1.2.3.4"));
        Assert.True(sut.TryRegisterAttempt("1.2.3.4"));
    }

    [Fact]
    public void TryRegisterAttempt_OverThreshold_Rejected()
    {
        var sut = NewLimiter(max: 3);
        sut.TryRegisterAttempt("1.2.3.4");
        sut.TryRegisterAttempt("1.2.3.4");
        sut.TryRegisterAttempt("1.2.3.4");
        Assert.False(sut.TryRegisterAttempt("1.2.3.4"));
        Assert.False(sut.TryRegisterAttempt("1.2.3.4"));
    }

    [Fact]
    public void TryRegisterAttempt_DifferentIps_TrackedSeparately()
    {
        var sut = NewLimiter(max: 2);
        sut.TryRegisterAttempt("1.1.1.1");
        sut.TryRegisterAttempt("1.1.1.1");
        // IP1 maxed
        Assert.False(sut.TryRegisterAttempt("1.1.1.1"));
        // IP2 fresh
        Assert.True(sut.TryRegisterAttempt("2.2.2.2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryRegisterAttempt_AnonymousIp_AlwaysAllowed(string? ip)
    {
        var sut = NewLimiter(max: 1);
        Assert.True(sut.TryRegisterAttempt(ip));
        Assert.True(sut.TryRegisterAttempt(ip));
        Assert.True(sut.TryRegisterAttempt(ip));
    }

    [Fact]
    public void Clear_ResetsCounterForIp()
    {
        var sut = NewLimiter(max: 2);
        sut.TryRegisterAttempt("9.9.9.9");
        sut.TryRegisterAttempt("9.9.9.9");
        Assert.False(sut.TryRegisterAttempt("9.9.9.9"));

        sut.Clear("9.9.9.9");
        Assert.True(sut.TryRegisterAttempt("9.9.9.9"));
    }

    [Fact]
    public void Clear_AnonymousIp_NoOp()
    {
        var sut = NewLimiter();
        sut.Clear(null);         // shouldn't throw
        sut.Clear("");           // shouldn't throw
    }

    [Fact]
    public void Ctor_InvalidMaxAttempts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LoginRateLimiter(new MemoryCache(new MemoryCacheOptions()), maxAttemptsPerWindow: 0));
    }
}
