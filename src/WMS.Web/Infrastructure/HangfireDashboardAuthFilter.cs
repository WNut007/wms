using Hangfire.Dashboard;

namespace WMS.Web.Infrastructure;

// Phase 17 — gate /hangfire dashboard. MVP: any authenticated user
// passes (Hangfire's IsAuthenticated check would do the same, but
// going through the custom filter makes the gate explicit + a single
// place to tighten when role-aware admin checks land — TD).
//
// Tightening path: inject IPermissionService here and require either
// (a) the user has SYSTEM.ADMIN perm, or (b) the user is in the
// ADMIN role (per Phase 1 seed). Until then, "must be logged in" is
// the floor — the dashboard exposes job state + retries, not data.
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true;
    }
}
