using System.ComponentModel.DataAnnotations;

namespace WMS.Web.ViewModels.SuperAdmin;

public sealed class SuperAdminLoginViewModel
{
    [Required, EmailAddress, StringLength(100)]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(100)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public sealed class SuperAdminChangePasswordViewModel
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
