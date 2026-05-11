using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Auth;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    // Set by [HttpGet] when the user is bounced from a protected page.
    // Round-tripped through a hidden field so a successful login can
    // resume the original navigation. Sanitised at use-site (IsLocalUrl)
    // to block open-redirect attempts.
    public string? ReturnUrl { get; set; }

    // Phase 25 — when true, the session cookie is issued with
    // IsPersistent + 30-day ExpiresUtc. False → cookie expires on browser
    // close (Session lifetime). Default false so the secure path is the
    // path of least resistance.
    [Display(Name = "Remember me on this device")]
    public bool RememberMe { get; set; }
}
