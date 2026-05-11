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
    private const string RememberMeCookieName = "wms.rememberme";
    private static readonly TimeSpan PreAuthCookieLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PersistentCookieLifetime = TimeSpan.FromDays(30);

    private readonly IAuthService _auth;
    private readonly IUserTenantMapRepository _userTenantMapRepo;
    private readonly IUserRepositoryFactory _userRepoFactory;
    private readonly IWarehouseRepositoryFactory _warehouseRepoFactory;

    public AuthController(
        IAuthService auth,
        IUserTenantMapRepository userTenantMapRepo,
        IUserRepositoryFactory userRepoFactory,
        IWarehouseRepositoryFactory warehouseRepoFactory)
    {
        _auth = auth;
        _userTenantMapRepo = userTenantMapRepo;
        _userRepoFactory = userRepoFactory;
        _warehouseRepoFactory = warehouseRepoFactory;
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
            // Phase 25 — RateLimited gets a distinct message so the
            // operator knows to back off. UnknownEmail vs InvalidPassword
            // still share a message to avoid email-enumeration leaks.
            // AccountLocked-equivalent failures arrive as InvalidPassword
            // (VerifyPasswordAsync returns null for both) — the audit
            // log differentiates them, but the operator-facing message
            // should not.
            ModelState.AddModelError(string.Empty,
                result.FailureReason == "RateLimited"
                    ? "Too many login attempts. Please wait a minute and try again."
                    : "Invalid email or password.");
            return View(model);
        }

        WritePreAuthCookie(result.PreAuthToken!);
        // Phase 25 — stash Remember Me intent in a parallel cookie so it
        // survives the 3-step flow. Read + cleared at SignInAsync time.
        if (model.RememberMe) WriteRememberMeCookie();

        // P0 #4 — user must change their password before continuing.
        // Token is flagged (RequiresPasswordChange=true). Redirect to the
        // in-flow change-password step on the same _AuthLayout shell;
        // session cookie is NOT issued yet, so no sidebar leakage.
        if (result.RequiresPasswordChange)
            return RedirectToAction(nameof(ChangePassword));

        return RedirectToAction(nameof(SelectTenant));
    }

    // P0 #4 — in-flow forced password change step. Renders inside
    // _AuthLayout (no sidebar / nav chrome). Cookie issuance is deferred
    // until the password is changed, so unlike the legacy redirect-to-
    // /Account/ChangePassword path the user cannot navigate elsewhere.
    [HttpGet]
    public async Task<IActionResult> ChangePassword(CancellationToken ct)
    {
        var (_, preAuth) = await ResolvePreAuthAsync(ct);
        if (preAuth is null)
            return RedirectToAction(nameof(Login));

        // Wrong-token-type guard: this endpoint only accepts forced-change
        // tokens. A regular preauth token (from a user without
        // MustChangePassword) means someone hit the URL directly mid-flow;
        // bounce them to the normal next step.
        if (!preAuth.RequiresPasswordChange)
            return RedirectToAction(nameof(SelectTenant));

        return View(new ForcedPasswordChangeViewModel { UserEmail = preAuth.UserEmail });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        ForcedPasswordChangeViewModel model,
        CancellationToken ct)
    {
        var (oldToken, preAuth) = await ResolvePreAuthAsync(ct);
        if (preAuth is null)
            return RedirectToAction(nameof(Login));

        if (!preAuth.RequiresPasswordChange)
            return RedirectToAction(nameof(SelectTenant));

        // Carry email forward so the re-render after a validation error
        // still shows the "set a new password for {email}" line.
        model.UserEmail = preAuth.UserEmail;

        if (!ModelState.IsValid)
            return View(model);

        var result = await _auth.ApplyForcedPasswordChangeAsync(
            oldToken!, model.NewPassword,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            ct);

        if (!result.Success)
        {
            // FailureReason is either a stable token ("InvalidToken",
            // "WrongTokenType", "UserNotFound") or a PasswordPolicy
            // message. Map the stable tokens to user-facing copy; pass
            // the policy message through verbatim.
            var msg = result.FailureReason switch
            {
                "InvalidToken" or "WrongTokenType" =>
                    "Your session expired. Please sign in again.",
                "UserNotFound" =>
                    "Your account is no longer available. Please contact your administrator.",
                _ => result.FailureReason ?? "Password could not be changed.",
            };

            if (result.FailureReason is "InvalidToken" or "WrongTokenType" or "UserNotFound")
            {
                // No useful state to re-render against — bounce to Login.
                ClearPreAuthCookie();
                TempData["LoginNotice"] = msg;
                return RedirectToAction(nameof(Login));
            }

            // Policy violation — keep the user on the form to fix it.
            ModelState.AddModelError(nameof(model.NewPassword), msg);
            return View(model);
        }

        // Success — swap the cookie to the new token (flag=false) and
        // re-enter the normal Step 2 chain. SelectTenant will smart-skip
        // when the user has a single tenant.
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
    public async Task<IActionResult> SelectWarehouse(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction(nameof(Login));

        if (!TryGetTenantId(out var tenantId))
            return RedirectToAction(nameof(Login));

        var repo = _warehouseRepoFactory.For(tenantId);

        // Phase 8.5 picker uses the richer projection (Code, Name,
        // Address, Type) for region grouping. The 0-warehouse + 1-
        // warehouse smart-skip branches still drive off the lighter
        // GetActiveAsync — those paths don't render the picker UI
        // and the projection difference is noise.
        var lightInfo = await repo.GetActiveAsync(ct);

        if (lightInfo.Count == 0)
        {
            // Defensive: tenant has no active warehouses configured. Sign
            // the user out so they're not stuck holding a half-good cookie,
            // and render an explicit "ask your admin" page.
            await HttpContext.SignOutAsync();
            return View("NoWarehouseAccess");
        }

        if (lightInfo.Count == 1)
        {
            // Smart-skip — single warehouse, no picker.
            return await CompleteWarehouseSelectionAsync(lightInfo[0]);
        }

        var items = await repo.GetPickerItemsAsync(ct);
        return View(new WarehouseSelectViewModel
        {
            Items = items,
            TenantCode = User.FindFirst(WmsClaimTypes.TenantCode)?.Value ?? "",
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectWarehouse(WarehouseSelectViewModel model, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction(nameof(Login));

        if (!TryGetTenantId(out var tenantId))
            return RedirectToAction(nameof(Login));

        var repo = _warehouseRepoFactory.For(tenantId);
        var warehouses = await repo.GetActiveAsync(ct);
        var selected = warehouses.FirstOrDefault(w => w.Id == model.SelectedWarehouseId);
        if (selected is null)
        {
            // Authoritative check — re-fetch and confirm. Never trust the
            // posted Id alone.
            ModelState.AddModelError(string.Empty, "Please select a warehouse.");
            var items = await repo.GetPickerItemsAsync(ct);
            return View(new WarehouseSelectViewModel
            {
                Items = items,
                TenantCode = User.FindFirst(WmsClaimTypes.TenantCode)?.Value ?? "",
            });
        }

        return await CompleteWarehouseSelectionAsync(selected);
    }

    // POST + antiforgery so a cross-site link can't sign someone out.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        ClearPreAuthCookie();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Forbidden() => View();

    // Final commit of Step 3 — re-issue the session cookie with all
    // existing claims plus WmsClaimTypes.WarehouseId. SignInAsync
    // *replaces* the principal, so existing claims must be carried
    // forward explicitly; any prior WarehouseId claim is dropped before
    // the new one is appended.
    private async Task<IActionResult> CompleteWarehouseSelectionAsync(WarehouseInfo warehouse)
    {
        // SignInAsync replaces the principal — carry every existing claim
        // forward, drop any prior Warehouse* claims (covers re-pick), and
        // append the freshly chosen Id + Code.
        var claims = User.Claims
            .Where(c => c.Type != WmsClaimTypes.WarehouseId
                     && c.Type != WmsClaimTypes.WarehouseCode)
            .ToList();
        claims.Add(new Claim(WmsClaimTypes.WarehouseId, warehouse.Id.ToString()));
        claims.Add(new Claim(WmsClaimTypes.WarehouseCode, warehouse.Code));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        // Phase 25 — Step 3 (Warehouse) finalises the session cookie. If
        // Remember Me was checked at Step 1, issue a persistent cookie
        // with 30-day ExpiresUtc; otherwise default (expires per
        // Program.cs cookie ExpireTimeSpan = 8h sliding).
        var authProps = BuildAuthProperties();
        ClearRememberMeCookie();
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            authProps);

        return Redirect("/");
    }

    private AuthenticationProperties BuildAuthProperties()
    {
        var rememberMe = Request.Cookies[RememberMeCookieName] == "1";
        return new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.Add(PersistentCookieLifetime) : null,
        };
    }

    private void WriteRememberMeCookie() =>
        Response.Cookies.Append(RememberMeCookieName, "1", new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = PreAuthCookieLifetime,    // matches preauth flow lifetime
            Path = "/Auth",
        });

    private void ClearRememberMeCookie() =>
        Response.Cookies.Delete(RememberMeCookieName, new CookieOptions { Path = "/Auth" });

    private bool TryGetTenantId(out Guid tenantId)
    {
        var raw = User.FindFirstValue(WmsClaimTypes.TenantId);
        return Guid.TryParse(raw, out tenantId);
    }

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
            new(WmsClaimTypes.TenantCode, selected.TenantCode),
        };
        // P0 #4 post-30A — both branches below are now BELT-AND-SUSPENDERS.
        // The in-flow forced-change path (AuthController.ChangePassword)
        // applies the new password via AuthService.ApplyForcedPasswordChange-
        // Async BEFORE re-entering this method, so `user.MustChangePassword`
        // is always false here under the primary flow. The branches remain
        // for the rare race where the User row's flag isn't yet cleared
        // when we re-read it (e.g. read replica lag in a future hot path) —
        // MustChangePasswordMiddleware then catches the next request.
        if (user.MustChangePassword)
            claims.Add(new Claim(WMS.Web.Multitenancy.MustChangePasswordMiddleware.ClaimType,
                WMS.Web.Multitenancy.MustChangePasswordMiddleware.TrueValue));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        if (user.MustChangePassword)
            return Redirect("/Account/ChangePassword");

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
