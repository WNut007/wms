using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WMS.Web.Auth;

namespace WMS.Web.Filters;

// Phase 27 — gate /SuperAdmin/ actions on the SuperAdmin cookie scheme.
// Unlike [Authorize] (defaults to the tenant scheme), this filter
// inspects HttpContext.User against the SuperAdminAuth principal.
//
// Pattern mirrors RequirePermissionAttribute (Phase 25): resolves
// the user's authenticated state from the dedicated scheme, refuses
// otherwise. Anonymous → ChallengeAsync redirects to /SuperAdmin/Login.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireSuperAdminAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var result = await context.HttpContext.AuthenticateAsync(SuperAdminAuthScheme.Name);
        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
        {
            await context.HttpContext.ChallengeAsync(SuperAdminAuthScheme.Name);
            context.Result = new EmptyResult();
            return;
        }
        // Replace HttpContext.User so downstream controllers see the
        // SuperAdmin principal (not the tenant principal if both
        // cookies happen to be present).
        context.HttpContext.User = result.Principal;
    }
}
