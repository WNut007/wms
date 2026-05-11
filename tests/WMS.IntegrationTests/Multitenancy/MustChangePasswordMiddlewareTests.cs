using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using WMS.Common.Auth;
using WMS.Web.Multitenancy;

namespace WMS.IntegrationTests.Multitenancy;

// Phase 29 — verify the middleware redirects when the claim is set,
// passes through when it isn't, and respects the allowlist (so the
// user can actually reach /Account/ChangePassword to clear the flag).
public class MustChangePasswordMiddlewareTests
{
    private static DefaultHttpContext BuildContext(string path, bool authenticated, bool mustChange, bool isTenantScheme)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();

        if (!authenticated) return ctx;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        };
        if (isTenantScheme)
            claims.Add(new Claim(WmsClaimTypes.TenantId, Guid.NewGuid().ToString()));
        if (mustChange)
            claims.Add(new Claim(MustChangePasswordMiddleware.ClaimType, MustChangePasswordMiddleware.TrueValue));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    private static async Task<(int StatusCode, string? Location, bool NextCalled)> RunAsync(HttpContext ctx)
    {
        var nextCalled = false;
        var mw = new MustChangePasswordMiddleware(c =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        await mw.InvokeAsync(ctx);
        return (ctx.Response.StatusCode, ctx.Response.Headers["Location"].ToString(), nextCalled);
    }

    [Fact]
    public async Task Anonymous_PassesThrough()
    {
        var ctx = BuildContext("/Dashboard", authenticated: false, mustChange: false, isTenantScheme: false);
        var (status, location, nextCalled) = await RunAsync(ctx);
        Assert.True(nextCalled);
        Assert.True(string.IsNullOrEmpty(location));
    }

    [Fact]
    public async Task Authenticated_NoClaim_PassesThrough()
    {
        var ctx = BuildContext("/Dashboard", authenticated: true, mustChange: false, isTenantScheme: true);
        var (_, location, nextCalled) = await RunAsync(ctx);
        Assert.True(nextCalled);
        Assert.True(string.IsNullOrEmpty(location));
    }

    [Fact]
    public async Task Authenticated_MustChange_RedirectsToChangePassword()
    {
        var ctx = BuildContext("/Dashboard", authenticated: true, mustChange: true, isTenantScheme: true);
        var (status, location, nextCalled) = await RunAsync(ctx);
        Assert.False(nextCalled);
        Assert.Equal("/Account/ChangePassword", location);
        Assert.Equal(302, status);
    }

    [Theory]
    [InlineData("/Account/ChangePassword")]
    [InlineData("/Account/ChangePassword/sub")]
    [InlineData("/Auth/Login")]
    [InlineData("/Auth/Logout")]
    [InlineData("/Auth/SelectTenant")]
    [InlineData("/SuperAdmin/Dashboard")]
    [InlineData("/healthz/ready")]
    [InlineData("/health")]
    [InlineData("/Error/500")]
    public async Task AllowedPaths_PassThrough_EvenWhenMustChange(string path)
    {
        var ctx = BuildContext(path, authenticated: true, mustChange: true, isTenantScheme: true);
        var (_, location, nextCalled) = await RunAsync(ctx);
        Assert.True(nextCalled);
        Assert.True(string.IsNullOrEmpty(location));
    }

    [Fact]
    public async Task NonTenantScheme_PassesThrough_EvenWhenMustChange()
    {
        // SuperAdmin user has no TenantId claim → middleware treats as
        // non-tenant scheme and skips. The SuperAdmin's own MustChange-
        // Password flow lives in SuperAdminAuthController.
        var ctx = BuildContext("/Dashboard", authenticated: true, mustChange: true, isTenantScheme: false);
        var (_, location, nextCalled) = await RunAsync(ctx);
        Assert.True(nextCalled);
        Assert.True(string.IsNullOrEmpty(location));
    }
}
