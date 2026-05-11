using System.ComponentModel.DataAnnotations;

namespace WMS.Web.ViewModels.SuperAdmin;

// P0 #4 — VM for the /SuperAdmin/ForcePasswordChange step. No
// CurrentPassword field — operator just entered it at /SuperAdmin/Login
// moments ago. Carry is via a DataProtector-encrypted cookie holding
// the SuperAdminId; session cookie is not yet issued.
public sealed class SuperAdminForcedPwChangeViewModel
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

    // Read-only — populated server-side from the carry cookie for
    // display ("Set a new password for {email}").
    public string? UserEmail { get; set; }
}
