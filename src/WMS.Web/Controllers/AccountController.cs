using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Security;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
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

            TempData["AccountMessage"] = "Password changed.";
            return RedirectToAction(nameof(ChangePassword));
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
}
