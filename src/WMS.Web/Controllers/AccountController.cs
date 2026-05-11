using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Security;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.Web.Multitenancy;
using WMS.Web.ViewModels.Security;

namespace WMS.Web.Controllers;

// Phase 25 — self-service account surface. Today: just password change.
// Future: profile edit, 2FA enrolment (TD-055), per-user preferences.
//
// Permission: [Authorize] only — every authenticated user can change
// their own password (no SECURITY.USERS perm needed; they're acting on
// themselves, not another user).
[Authorize]
[Route("Account")]
public sealed class AccountController : Controller
{
    private readonly ISecurityService _security;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public AccountController(
        ISecurityService security,
        ITenantContext tenant,
        ICurrentUser currentUser)
    {
        _security = security;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    [HttpGet("ChangePassword")]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost("ChangePassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordViewModel model,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View(model);

        // Belt-and-suspenders policy check at controller boundary so
        // the inline error renders against NewPassword, not as a
        // form-level summary.
        var policyError = PasswordPolicy.Validate(model.NewPassword);
        if (policyError is not null)
        {
            ModelState.AddModelError(nameof(model.NewPassword), policyError);
            return View(model);
        }

        // Capture the must-change state BEFORE the re-sign-in clears it.
        var wasForcedChange = User.FindFirst(MustChangePasswordMiddleware.ClaimType)?.Value
            == MustChangePasswordMiddleware.TrueValue;

        try
        {
            await _security.ChangePasswordAsync(
                _tenant.RequireTenantId(),
                _currentUser.UserId ?? Guid.Empty,
                model.CurrentPassword,
                model.NewPassword,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString(),
                ct);

            // Phase 29 — re-issue the cookie WITHOUT the MustChangePassword
            // claim so the middleware stops redirecting. UpdatePasswordHash-
            // Async already cleared the DB flag; this syncs the in-flight
            // session. If we don't do this, the operator sees the success
            // banner but gets bounced back here on the next click.
            await RefreshClaimsWithoutMustChangePasswordAsync();

            TempData["AccountMessage"] = "Password changed.";
            // Forced-change path → bootstrap admin lands on dashboard.
            // Voluntary-change path → stays on the form for the success banner.
            return wasForcedChange ? Redirect("/") : RedirectToAction(nameof(ChangePassword));
        }
        catch (ArgumentException ex)
        {
            // PasswordPolicy.ThrowIfInvalid uses ArgumentException; map to
            // the NewPassword field for inline display.
            ModelState.AddModelError(nameof(model.NewPassword), ex.Message);
            return View(model);
        }
        catch (InvalidOperationException ex)
        {
            // "Current password is incorrect", "Account is inactive", etc.
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    // Phase 29 — re-issue the cookie minus the MustChangePassword claim.
    // SignInAsync replaces the cookie entirely; copy every existing
    // claim forward except the one we want to drop.
    private async Task RefreshClaimsWithoutMustChangePasswordAsync()
    {
        var claims = User.Claims
            .Where(c => c.Type != MustChangePasswordMiddleware.ClaimType)
            .ToList();
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}
