using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
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
//
// P0 #4 — for the forced first-login change, the controller defers
// SignInAsync until AFTER the change. SuperAdminId is carried across
// the redirect via a DataProtector-encrypted short-lived cookie
// (wms.super_force_pw) instead of session auth, so _SuperAdminLayout's
// nav strip (which renders only on User.IsAuthenticated=true) stays
// hidden. No bypass surface.
[Route("SuperAdmin")]
public sealed class SuperAdminAuthController : Controller
{
    private const string ForcedPwCookieName = "wms.super_force_pw";
    private static readonly TimeSpan ForcedPwCookieLifetime = TimeSpan.FromMinutes(5);

    private readonly ISuperAdminAuthService _auth;
    private readonly IDataProtector _forcedPwProtector;

    public SuperAdminAuthController(
        ISuperAdminAuthService auth,
        IDataProtectionProvider dp)
    {
        _auth = auth;
        // Versioned purpose string so rotating the carry shape later
        // invalidates outstanding cookies safely.
        _forcedPwProtector = dp.CreateProtector("WMS.SuperAdmin.ForcedPwChange.v1");
    }

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

        // P0 #4 — defer SignInAsync when admin must change password.
        // Carry SuperAdminId via DataProtector-encrypted cookie; the
        // forced-change endpoint reads it without [RequireSuperAdmin]
        // (cookie is the auth, no session principal yet). No session
        // cookie = no nav chrome = no bypass.
        if (admin.MustChangePassword)
        {
            WriteForcedPwCookie(admin.Id);
            return RedirectToAction(nameof(ForcePasswordChange));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Email, admin.Email),
            new(ClaimTypes.Name, admin.FullName ?? admin.Email),
            new("MustChangePassword", "false"),
        };
        var identity = new ClaimsIdentity(claims, SuperAdminAuthScheme.Name);
        await HttpContext.SignInAsync(
            SuperAdminAuthScheme.Name,
            new ClaimsPrincipal(identity));

        return Redirect(IsSafeReturnUrl(model.ReturnUrl) ? model.ReturnUrl! : "/SuperAdmin/Dashboard");
    }

    // P0 #4 — in-flow forced password change for SuperAdmin first login.
    // NOT gated by [RequireSuperAdmin]: the auth is the encrypted
    // wms.super_force_pw cookie carrying the SuperAdminId.
    //
    // GET handler is pure render — no service call needed. Identity
    // validation happens at POST time when we actually apply the
    // change. If the cookie is missing / tampered / expired, fall
    // through to Login.
    [HttpGet("ForcePasswordChange")]
    public IActionResult ForcePasswordChange()
    {
        if (!TryReadForcedPwCookie(out _))
            return RedirectToAction(nameof(Login));

        return View(new SuperAdminForcedPwChangeViewModel());
    }

    [HttpPost("ForcePasswordChange")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForcePasswordChange(
        SuperAdminForcedPwChangeViewModel model,
        CancellationToken ct = default)
    {
        if (!TryReadForcedPwCookie(out var adminId))
            return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
            return View(model);

        var result = await _auth.ApplyForcedPasswordChangeAsync(
            adminId, model.NewPassword,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            ct);

        if (!result.Success)
        {
            if (result.FailureReason is "UserNotFound" or "WrongTokenType")
            {
                ClearForcedPwCookie();
                TempData["LoginNotice"] = result.FailureReason == "WrongTokenType"
                    ? "Your account no longer requires a password change. Please sign in."
                    : "Your account is no longer available. Please contact your administrator.";
                return RedirectToAction(nameof(Login));
            }

            // Policy violation — stay on form so user can fix it.
            ModelState.AddModelError(nameof(model.NewPassword),
                result.FailureReason ?? "Password could not be changed.");
            return View(model);
        }

        // Success — sign the admin in (MustChangePassword now false in
        // DB + on the returned entity) and clear the carry cookie.
        var admin = result.Admin!;
        ClearForcedPwCookie();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Email, admin.Email),
            new(ClaimTypes.Name, admin.FullName ?? admin.Email),
            new("MustChangePassword", "false"),
        };
        var identity = new ClaimsIdentity(claims, SuperAdminAuthScheme.Name);
        await HttpContext.SignInAsync(
            SuperAdminAuthScheme.Name,
            new ClaimsPrincipal(identity));

        return Redirect("/SuperAdmin/Dashboard");
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

    private void WriteForcedPwCookie(Guid superAdminId)
    {
        var payload = _forcedPwProtector.Protect(superAdminId.ToString());
        Response.Cookies.Append(ForcedPwCookieName, payload, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = ForcedPwCookieLifetime,
            Path = "/SuperAdmin",
        });
    }

    private void ClearForcedPwCookie() =>
        Response.Cookies.Delete(ForcedPwCookieName, new CookieOptions { Path = "/SuperAdmin" });

    private bool TryReadForcedPwCookie(out Guid superAdminId)
    {
        superAdminId = Guid.Empty;
        var payload = Request.Cookies[ForcedPwCookieName];
        if (string.IsNullOrEmpty(payload)) return false;

        try
        {
            var raw = _forcedPwProtector.Unprotect(payload);
            return Guid.TryParse(raw, out superAdminId);
        }
        catch (CryptographicException)
        {
            // Tampered, expired, or signed with a different key.
            return false;
        }
    }
}
