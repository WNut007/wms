using Microsoft.AspNetCore.Http;
using WMS.Web.Infrastructure;

namespace WMS.IntegrationTests.Infrastructure;

// Phase 26 — verify ApplyHeaders sets the configured headers, skips
// empty-config headers, removes the Server fingerprint, and doesn't
// overwrite downstream-set headers. Tested at the static-method level
// — DefaultHttpContext's OnStarting callbacks don't fire on a
// MemoryStream body write, so the static extract is the testable seam.
public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public void ApplyHeaders_SetsAllConfiguredHeaders()
    {
        var ctx = new DefaultHttpContext();
        var opts = new SecurityHeadersOptions
        {
            FrameOptions = "DENY",
            ContentTypeOptions = "nosniff",
            ReferrerPolicy = "strict-origin-when-cross-origin",
            ContentSecurityPolicy = "default-src 'self'",
            PermissionsPolicy = "camera=()",
        };

        SecurityHeadersMiddleware.ApplyHeaders(ctx.Response.Headers, opts);

        Assert.Equal("DENY", ctx.Response.Headers["X-Frame-Options"]);
        Assert.Equal("nosniff", ctx.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", ctx.Response.Headers["Referrer-Policy"]);
        Assert.Equal("default-src 'self'", ctx.Response.Headers["Content-Security-Policy"]);
        Assert.Equal("camera=()", ctx.Response.Headers["Permissions-Policy"]);
    }

    [Fact]
    public void ApplyHeaders_EmptyConfigValue_SkipsHeader()
    {
        var ctx = new DefaultHttpContext();
        var opts = new SecurityHeadersOptions
        {
            FrameOptions = "DENY",
            ContentTypeOptions = null,      // skip
            ReferrerPolicy = "",            // skip
            ContentSecurityPolicy = "   ",  // skip (whitespace)
            PermissionsPolicy = "camera=()",
        };

        SecurityHeadersMiddleware.ApplyHeaders(ctx.Response.Headers, opts);

        Assert.Equal("DENY", ctx.Response.Headers["X-Frame-Options"]);
        Assert.False(ctx.Response.Headers.ContainsKey("X-Content-Type-Options"));
        Assert.False(ctx.Response.Headers.ContainsKey("Referrer-Policy"));
        Assert.False(ctx.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.Equal("camera=()", ctx.Response.Headers["Permissions-Policy"]);
    }

    [Fact]
    public void ApplyHeaders_RemovesServerHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Headers["Server"] = "Kestrel/8.0";

        SecurityHeadersMiddleware.ApplyHeaders(ctx.Response.Headers, new SecurityHeadersOptions());

        Assert.False(ctx.Response.Headers.ContainsKey("Server"));
    }

    [Fact]
    public void ApplyHeaders_DoesNotOverwriteExistingDownstreamHeader()
    {
        // If a controller sets its own CSP (e.g. pack-video endpoint),
        // middleware should NOT overwrite it.
        var ctx = new DefaultHttpContext();
        ctx.Response.Headers["Content-Security-Policy"] = "custom-policy";

        var opts = new SecurityHeadersOptions { ContentSecurityPolicy = "default-src 'self'" };
        SecurityHeadersMiddleware.ApplyHeaders(ctx.Response.Headers, opts);

        Assert.Equal("custom-policy", ctx.Response.Headers["Content-Security-Policy"]);
    }

    [Fact]
    public async Task InvokeAsync_CallsNext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var nextCalled = false;

        var mw = new SecurityHeadersMiddleware(
            next: c => { nextCalled = true; return Task.CompletedTask; },
            options: new SecurityHeadersOptions { FrameOptions = "DENY" });

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
        // OnStarting registration is verifiable in production (TestServer);
        // the actual header set is exercised in ApplyHeaders unit tests.
    }
}
