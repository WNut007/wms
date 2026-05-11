using System.Security.Claims;
using Hangfire.Dashboard;
using WMS.Common.Auth;
using WMS.DAL.Repositories.Security;

namespace WMS.Web.Infrastructure;

// Phase 17 + Phase 25 — gate /hangfire dashboard. Authenticated +
// ADMIN role required.
//
// Phase 25 tightens the Phase 17 MVP (which only required
// IsAuthenticated) by checking the user holds the ADMIN role in
// their primary tenant. Implementation matches the
// SecurityService.ToggleUserActiveAsync last-admin guard pattern:
// read UserRoles → Roles → look for Code='ADMIN'.
//
// Sync-over-async caveat: IDashboardAuthorizationFilter.Authorize is
// synchronous (IDashboardAsyncAuthorizationFilter exists in newer
// Hangfire versions; the codebase is on 1.8.14). Two Dapper round-
// trips at worst; production load on /hangfire is low (admin-only
// by definition) so .GetAwaiter().GetResult() is acceptable.
//
// Failure mode: anonymous or non-admin → Hangfire returns 401/403.
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    private const string AdminRoleCode = "ADMIN";

    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (http.User.Identity?.IsAuthenticated != true) return false;

        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(http.User.FindFirstValue(WmsClaimTypes.TenantId), out var tenantId))
        {
            return false;
        }

        // Resolve repos from request scope. Mirrors RequirePermission-
        // Attribute's pattern (it does the same with IPermissionService).
        var userRoleFactory = http.RequestServices.GetRequiredService<IUserRoleRepositoryFactory>();
        var roleFactory = http.RequestServices.GetRequiredService<IRoleRepositoryFactory>();

        var userRoles = userRoleFactory.For(tenantId);
        var roles = roleFactory.For(tenantId);

        // Sync over async — acceptable here (see class comment).
        var roleIds = userRoles.GetRoleIdsByUserAsync(userId).GetAwaiter().GetResult();
        foreach (var rid in roleIds)
        {
            var role = roles.GetByIdAsync(rid).GetAwaiter().GetResult();
            if (role is not null
                && string.Equals(role.Code, AdminRoleCode, StringComparison.Ordinal)
                && role.IsActive)
            {
                return true;
            }
        }
        return false;
    }
}
