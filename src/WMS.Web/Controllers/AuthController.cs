using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Auth;
using WMS.Web.Models.Auth;

namespace WMS.Web.Controllers;

// Step 1 of the 3-step login flow (ADR-008): email + password →
// pre-auth token (carried via a short-lived HttpOnly cookie) →
// hand off to /Auth/SelectTenant for Step 2.
//
// Step 2 (tenant select) and Step 3 (warehouse select) are stubs in
// this chunk — A5/A6 will replace them.
public sealed class AuthController : BaseController
{
    private const string PreAuthCookieName = "wms.preauth";
    private static readonly TimeSpan PreAuthCookieLifetime = TimeSpan.FromMinutes(5);

    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

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

        // Pre-auth cookie carries the token to Step 2. Path scoped to
        // /Auth so it's never sent to anything else; expires at the same
        // moment the master.PreAuthTokens row does.
        Response.Cookies.Append(PreAuthCookieName, result.PreAuthToken!,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                MaxAge = PreAuthCookieLifetime,
                Path = "/Auth",
            });

        return RedirectToAction(nameof(SelectTenant));
    }

    [HttpGet]
    public IActionResult SelectTenant()
    {
        // Stub for A4 — A5 replaces this with the real picker that
        // validates the cookie token, auto-selects when the user has a
        // single tenant, and otherwise shows the list.
        if (string.IsNullOrEmpty(Request.Cookies[PreAuthCookieName]))
            return RedirectToAction(nameof(Login));

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        Response.Cookies.Delete(PreAuthCookieName, new CookieOptions { Path = "/Auth" });
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Forbidden() => View();
}
