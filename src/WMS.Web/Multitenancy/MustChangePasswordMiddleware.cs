using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace WMS.Web.Multitenancy;

// Phase 29 (P0 fix) — enforce the MustChangePassword flag for tenant
// users. When a bootstrap admin is provisioned via SuperAdmin tenant
// onboarding (Phase 27), their User row gets MustChangePassword=true.
// Without this middleware, the bootstrap admin could keep using the
// temp password indefinitely — Phase 27 D4 said "first login redirects
// to /Account/ChangePassword; cannot proceed until changed".
//
// The flag is carried as a custom claim ("MustChangePassword") set at
// SignInAsync time in AuthController.CompleteTenantSelectionAsync. When
// the claim is "true", every request EXCEPT a small allowlist is
// redirected to /Account/ChangePassword. The claim is cleared by
// re-signing-in at the end of AccountController.ChangePassword.
//
// Mirrors the SuperAdmin-side enforcement that already happens in
// SuperAdminAuthController.Login post-auth.
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
