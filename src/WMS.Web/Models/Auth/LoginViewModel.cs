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
}
