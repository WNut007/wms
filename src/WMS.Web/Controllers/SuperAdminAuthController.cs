using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Security;
using WMS.BLL.Services.SuperAdmin;
using WMS.Web.Auth;
using WMS.Web.Filters;
using WMS.Web.ViewModels.SuperAdmin;

namespace WMS.Web.Controllers;

// Phase 27 — /SuperAdmin/Login + /SuperAdmin/Logout. Single-step auth
// (no tenant select — SuperAdmins are cross-tenant). Forces password
// change on first login via MustChangePassword check.
//
// Distinct from the tenant /Auth/* flow. Uses the SuperAdminAuth
// cookie scheme so the two principals don't overlap.
[Route("SuperAdmin")]
public sealed class SuperAdminAuthController : Controller
{
    private readonly ISuperAdminAuthService _auth;

    public SuperAdminAuthController(ISuperAdminAuthService auth) => _auth = auth;

    [HttpGet("Login")]
    public IActionResult Login(string? returnUrl = null) =>
        View(new SuperAdminLoginViewModel { ReturnUrl = returnUrl });

    [HttpPost("Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        SuperAdminLoginViewModel model,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _auth.AuthenticateAsync(
            model.Email,
            model.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            ct);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty,
                result.FailureReason == "RateLimited"
                    ? "Too many login attempts. Please wait a minute and try again."
                    : "Invalid email or password.");
            return View(model);
        }

        var admin = result.Admin!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Email, admin.Email),
            new(ClaimTypes.Name, admin.FullName ?? admin.Email),
            new("MustChangePassword", admin.MustChangePassword ? "true" : "false"),
        };
        var identity = new ClaimsIdentity(claims, SuperAdminAuthScheme.Name);
        await HttpContext.SignInAsync(
            SuperAdminAuthScheme.Name,
            new ClaimsPrincipal(identity));

        // First-login force-change interceptor — operator can't proceed
        // until they rotate the temp password.
        if (admin.MustChangePassword)
            return RedirectToAction(nameof(ChangePassword));

        return Redirect(IsSafeReturnUrl(model.ReturnUrl) ? model.ReturnUrl! : "/SuperAdmin/Dashboard");
    }

    [HttpPost("Logout")]
    [ValidateAntiForgeryToken]
    [RequireSuperAdmin]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(SuperAdminAuthScheme.Name);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("ChangePassword")]
    [RequireSuperAdmin]
    public IActionResult ChangePassword() =>
        View(new SuperAdminChangePasswordViewModel());

    [HttpPost("ChangePassword")]
    [ValidateAntiForgeryToken]
    [RequireSuperAdmin]
    public async Task<IActionResult> ChangePassword(
        SuperAdminChangePasswordViewModel model,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(model);

        var policyError = PasswordPolicy.Validate(model.NewPassword);
        if (policyError is not null)
        {
            ModelState.AddModelError(nameof(model.NewPassword), policyError);
            return View(model);
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            // Should never happen — RequireSuperAdmin ensured authn —
            // but defend.
            return RedirectToAction(nameof(Login));
        }

        try
        {
            await _auth.ChangePasswordAsync(
                adminId,
                model.CurrentPassword,
                model.NewPassword,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                ct);
            TempData["SuperAdminMessage"] = "Password changed.";
            return RedirectToAction(nameof(ChangePassword));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(model.NewPassword), ex.Message);
            return View(model);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    private bool IsSafeReturnUrl(string? url) =>
        !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url);
}
