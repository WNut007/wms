using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Security;
using WMS.Web.Models.Auth;

namespace WMS.Web.Controllers;

// 3-step login flow (ADR-008):
//
//   Step 1 — POST /Auth/Login          → AuthenticateAsync
//                                       → set wms.preauth cookie
//                                       → 302 /Auth/SelectTenant
//   Step 2 — GET  /Auth/SelectTenant   → smart-skip if 1 tenant,
//                                         else render picker
//             POST /Auth/SelectTenant  → SignInAsync with TenantId claim
//                                       → 302 /Auth/SelectWarehouse
//   Step 3 — /Auth/SelectWarehouse     → A6 (stub here for now)
public sealed class AuthController : BaseController
{
    private const string PreAuthCookieName = "wms.preauth";
    private static readonly TimeSpan PreAuthCookieLifetime = TimeSpan.FromMinutes(5);

    private readonly IAuthService _auth;
    private readonly IUserTenantMapRepository _userTenantMapRepo;
    private readonly IUserRepositoryFactory _userRepoFactory;

    public AuthController(
        IAuthService auth,
        IUserTenantMapRepository userTenantMapRepo,
        IUserRepositoryFactory userRepoFactory)
    {
        _auth = auth;
        _userTenantMapRepo = userTenantMapRepo;
        _userRepoFactory = userRepoFactory;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _auth.AuthenticateAsync(
            model.Email,
            model.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            ct);

        if (!result.Success)
        {
            // Same message regardless of FailureReason — don't tell the
            // client whether the email is known. (Timing differs between
            // UnknownEmail vs InvalidPassword; tightening that is a
            // separate concern.)
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        WritePreAuthCookie(result.PreAuthToken!);
        return RedirectToAction(nameof(SelectTenant));
    }

    [HttpGet]
    public async Task<IActionResult> SelectTenant(CancellationToken ct)
    {
        var (token, preAuth) = await ResolvePreAuthAsync(ct);
        if (preAuth is null) return RedirectToAction(nameof(Login));

        var tenants = await _userTenantMapRepo.GetByEmailAsync(preAuth.UserEmail, ct);
        if (tenants.Count == 0)
        {
            // Race: tenant access removed between Step 1 and Step 2.
            ClearPreAuthCookie();
            return RedirectToAction(nameof(Login));
        }

        if (tenants.Count == 1)
        {
            // Smart-skip — single tenant, no picker. GET-with-side-effect
            // is acceptable here because pre-auth tokens are one-shot
            // anyway: re-running the GET wouldn't re-issue anything.
            return await CompleteTenantSelectionAsync(token!, preAuth, tenants[0], ct);
        }

        return View(new TenantSelectViewModel { Tenants = tenants });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectTenant(TenantSelectViewModel model, CancellationToken ct)
    {
        var (token, preAuth) = await ResolvePreAuthAsync(ct);
        if (preAuth is null) return RedirectToAction(nameof(Login));

        var tenants = await _userTenantMapRepo.GetByEmailAsync(preAuth.UserEmail, ct);
        var selected = tenants.FirstOrDefault(t => t.TenantId == model.SelectedTenantId);
        if (selected is null)
        {
            // Authoritative check — never trust the posted Id alone. If
            // it's not in the user's list, treat as a validation failure
            // and re-render with the real list.
            ModelState.AddModelError(string.Empty, "Please select a tenant.");
            return View(new TenantSelectViewModel { Tenants = tenants });
        }

        return await CompleteTenantSelectionAsync(token!, preAuth, selected, ct);
    }

    [HttpGet]
    public IActionResult SelectWarehouse()
    {
        // A6 stub. Full session cookie has the TenantId claim by the time
        // we get here; the next chunk replaces this with the real picker
        // (with its own smart-skip when the user has one warehouse).
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction(nameof(Login));

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        ClearPreAuthCookie();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Forbidden() => View();

    // Final commit of Step 2 — issue the full session cookie with the
    // chosen TenantId, mark the pre-auth token used (one-shot), drop the
    // pre-auth cookie. Shared between smart-skip and the POST handler.
    private async Task<IActionResult> CompleteTenantSelectionAsync(
        string preAuthToken,
        PreAuthData preAuth,
        UserTenantInfo selected,
        CancellationToken ct)
    {
        // The same email may have a different UserId per tenant DB —
        // look up the User in the *selected* tenant for the
        // NameIdentifier claim.
        var userRepo = _userRepoFactory.For(selected.TenantId);
        var user = await userRepo.GetByEmailAsync(preAuth.UserEmail, ct);
        if (user is null || !user.IsActive)
        {
            // Defensive: data inconsistency between master.UserTenantMap
            // and the tenant's security.Users (e.g. user disabled in the
            // tenant DB after the map row was added).
            ClearPreAuthCookie();
            return RedirectToAction(nameof(Login));
        }

        await _auth.MarkPreAuthTokenUsedAsync(preAuthToken, ct);
        ClearPreAuthCookie();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName ?? user.Email),
            new(WmsClaimTypes.TenantId, selected.TenantId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return RedirectToAction(nameof(SelectWarehouse));
    }

    // Reads + validates the pre-auth cookie. Returns (token, payload) on
    // success; (null, null) — and clears any stale cookie — on miss /
    // expiry / already-used.
    private async Task<(string? Token, PreAuthData? PreAuth)> ResolvePreAuthAsync(CancellationToken ct)
    {
        var token = Request.Cookies[PreAuthCookieName];
        if (string.IsNullOrEmpty(token))
            return (null, null);

        var preAuth = await _auth.ValidatePreAuthTokenAsync(token, ct);
        if (preAuth is null)
        {
            ClearPreAuthCookie();
            return (null, null);
        }

        return (token, preAuth);
    }

    private void WritePreAuthCookie(string token) =>
        Response.Cookies.Append(PreAuthCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = PreAuthCookieLifetime,
            Path = "/Auth",
        });

    private void ClearPreAuthCookie() =>
        Response.Cookies.Delete(PreAuthCookieName, new CookieOptions { Path = "/Auth" });
}
