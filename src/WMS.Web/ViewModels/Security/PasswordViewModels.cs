using System.ComponentModel.DataAnnotations;

namespace WMS.Web.ViewModels.Security;

// Phase 25 — self-service password change. Server-side rule enforcement
// happens in SecurityService.ChangePasswordAsync via PasswordPolicy;
// DataAnnotations here drive jQuery unobtrusive for instant client
// feedback.
public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

// Admin force-reset. No current password — admin has SECURITY.USERS.Edit
// authority. Target user is in the route, not the body.
public sealed class ResetPasswordViewModel
{
    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}
