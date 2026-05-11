using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Auth;

// P0 #4 — model for the in-flow forced password change step.
//
// The Token field is NOT round-tripped from the user; it's read
// from the pre-auth cookie on each request. We don't even need it
// in this VM — it lives here as `null` only to keep the form shape
// uniform with other auth models.
public sealed class ForcedPasswordChangeViewModel
{
    [Required(ErrorMessage = "New password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Confirm your new password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";

    // Read-only; populated by the GET handler so the view can show
    // "Set a new password for {email}" — confirmation for the operator
    // that they're working on the right account.
    public string? UserEmail { get; set; }
}
