using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;

namespace WMS.Web.Filters;

// Authorisation filter that gates an action / controller on a single
// (Function, Action) permission. Resolves IPermissionService from
// HttpContext so the attribute itself stays free of constructor deps
// and can be applied as a literal `[RequirePermission(...)]`.
//
// Decision matrix:
//   * Anonymous request                → ChallengeResult
//                                        (cookie scheme redirects to
//                                         LoginPath = /Auth/Login)
//   * Authenticated, claims malformed  → ForbidResult
//                                        (cookie scheme redirects to
//                                         AccessDeniedPath = /Auth/Forbidden)
//   * Authenticated, no permission     → ForbidResult (same redirect)
//   * Authenticated, has permission    → falls through to the action
//
// Use the constants on PermissionAction (View / Add / Edit / Delete /
// Approve) for the action argument so typos surface at compile time.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _functionCode;
    private readonly string _action;

    public RequirePermissionAttribute(string functionCode, string action)
    {
        _functionCode = functionCode;
        _action = action;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(user.FindFirstValue(WmsClaimTypes.TenantId), out var tenantId))
        {
            // Authenticated but session is missing the claims this filter
            // needs — partial Step 2/3 state, or a hand-crafted cookie.
            // Treat as a forbidden access rather than a redirect to login
            // so the user sees an explicit denial.
            context.Result = new ForbidResult();
            return;
        }

        var permService = context.HttpContext.RequestServices
            .GetRequiredService<IPermissionService>();

        var allowed = await permService.HasPermissionAsync(
            userId,
            tenantId,
            _functionCode,
            _action,
            context.HttpContext.RequestAborted);

        if (!allowed)
            context.Result = new ForbidResult();
    }
}
