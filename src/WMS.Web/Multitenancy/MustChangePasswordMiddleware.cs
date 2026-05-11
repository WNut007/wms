using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace WMS.Web.Multitenancy;

// Phase 29 (P0 fix) → P0 #4 post-30A: SAFETY NET only.
//
// PRIMARY path (P0 #4 post-30A): forced password change is now handled
// in-flow at login time by AuthController.ChangePassword. Cookie
// issuance is deferred until after the change, so users in this state
// never have a session cookie — the sidebar simply doesn't render and
// no bypass surface exists. The MustChangePassword claim is NOT set
// on cookies issued under the new flow.
//
// FALLBACK path (this middleware): catches users whose cookies were
// issued under the legacy Phase 29 path (claim=true) — typically a
// long-running session from before the fix was deployed, or the rare
// case where an admin force-flips a user's MustChangePassword=true
// during a live session. Those users get redirected here to
// /Account/ChangePassword to complete the change.
//
// The claim is cleared by re-signing-in at the end of
// AccountController.ChangePassword (Phase 29
// RefreshClaimsWithoutMustChangePasswordAsync). New cookies have no
// claim, the middleware no-ops, normal flow resumes.
//
// Allowlisted paths (must NOT redirect, else infinite loop / lockout):
//   /Account/ChangePassword       — the destination itself
//   /Auth/Logout                  — user must be able to escape
//   /SuperAdmin/*                 — separate cookie scheme; not our concern
//   /healthz, /health, /Error     — anonymous / system endpoints
//   GET /Account/ChangePassword   — render the form
//   POST /Account/ChangePassword  — submit the change
public sealed class MustChangePasswordMiddleware
{
    public const string ClaimType = "MustChangePassword";
    public const string TrueValue = "true";

    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Anonymous → no claim to check
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        if (!IsTenantSchemeUser(context))
        {
            // SuperAdmin or other scheme — has its own MustChangePassword
            // flow (SuperAdminAuthController). Skip.
            await _next(context);
            return;
        }

        var flag = context.User.FindFirstValue(ClaimType);
        if (!string.Equals(flag, TrueValue, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (IsAllowlisted(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Force-redirect to the change-password form. 302 (default) so
        // browsers preserve the operator's session.
        context.Response.Redirect("/Account/ChangePassword");
    }

    private static bool IsTenantSchemeUser(HttpContext context)
    {
        // Only the default cookie scheme (tenant auth) carries the
        // TenantId claim. SuperAdmin principals don't.
        return !string.IsNullOrEmpty(
            context.User.FindFirstValue(WMS.Common.Auth.WmsClaimTypes.TenantId));
    }

    private static bool IsAllowlisted(PathString path) =>
        path.StartsWithSegments("/Account/ChangePassword")
        || path.StartsWithSegments("/Auth")          // login / logout / select tenant / warehouse
        || path.StartsWithSegments("/SuperAdmin")    // separate scheme; no overlap
        || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/healthz")
        || path.StartsWithSegments("/Error");
}

public static class MustChangePasswordMiddlewareExtensions
{
    public static IApplicationBuilder UseMustChangePassword(this IApplicationBuilder app) =>
        app.UseMiddleware<MustChangePasswordMiddleware>();
}
