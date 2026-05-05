using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Serilog.Context;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;

namespace WMS.Web.Multitenancy;

// Runs on every request after UseAuthentication. For authenticated
// requests carrying a TenantId claim, it ensures the named tenant is
// still Active (status check via ITenantStatusReader, cached upstream)
// and pushes {TenantId, UserId} onto the Serilog log scope so every
// log line emitted downstream is tagged with tenant context.
//
// If the claimed tenant is no longer active (suspended / deleted),
// the user is signed out and redirected to /Auth/Login. This keeps
// stale cookies from accessing a tenant DB that's been taken offline.
//
// Skipped paths — these run before / outside any authenticated tenant
// flow:
//   * /health        — anonymous liveness probe.
//   * /Auth/*        — login, logout, tenant select, warehouse select;
//                       must work without a valid tenant claim (or any
//                       tenant claim, in the case of step 1).
public sealed class TenantValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantStatusReader _statusReader;
    private readonly ILogger<TenantValidationMiddleware> _logger;

    public TenantValidationMiddleware(
        RequestDelegate next,
        ITenantStatusReader statusReader,
        ILogger<TenantValidationMiddleware> logger)
    {
        _next = next;
        _statusReader = statusReader;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context))
        {
            await _next(context);
            return;
        }

        var tenantClaim = context.User.FindFirstValue(WmsClaimTypes.TenantId);
        if (string.IsNullOrEmpty(tenantClaim) || !Guid.TryParse(tenantClaim, out var tenantId))
        {
            // Authenticated but no tenant claim yet (mid-flow between
            // login step 1 and step 2). Let the request through —
            // controllers / authorization handle the partial-session case.
            await _next(context);
            return;
        }

        if (!await _statusReader.IsActiveAsync(tenantId, context.RequestAborted))
        {
            _logger.LogWarning(
                "Tenant {TenantId} is not Active for user {UserId}; signing out",
                tenantId,
                context.User.FindFirstValue(ClaimTypes.NameIdentifier));

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/Auth/Login");
            return;
        }

        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        using (LogContext.PushProperty("TenantId", tenantId))
        using (LogContext.PushProperty("UserId", userIdClaim))
        {
            await _next(context);
        }
    }

    private static bool ShouldSkip(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return true;

        var path = context.Request.Path;
        return path.StartsWithSegments("/health")
            || path.StartsWithSegments("/Auth");
    }
}
