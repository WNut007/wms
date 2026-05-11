using System.ComponentModel.DataAnnotations;

namespace WMS.Web.ViewModels.Security;

// Phase 24 — model-binding shapes for /Users Create + Edit.
// DataAnnotations drive client-side jQuery unobtrusive; FluentValidation
// (UserCreateValidator / UserEditValidator) handles server-side rules.

public sealed class UserCreateViewModel
{
    [Required, EmailAddress, StringLength(100)]
    public string Email { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [StringLength(200)]
    public string? FullName { get; set; }

    [Range(0, 9999999999.99)]
    public decimal? ApprovalLimit { get; set; }

    // Roles to assign at creation. Submitted as checkbox list. Empty
    // allowed but discouraged (operator with no roles has no permissions).
    public List<Guid> RoleIds { get; set; } = new();
}

public sealed class UserEditViewModel
{
    [Required]
    public Guid Id { get; set; }

    [Required, EmailAddress, StringLength(100)]
    public string Email { get; set; } = "";

    [StringLength(200)]
    public string? FullName { get; set; }

    [Range(0, 9999999999.99)]
    public decimal? ApprovalLimit { get; set; }

    public List<Guid> RoleIds { get; set; } = new();
}

// Display-only model for the Detail page sidebar.
public sealed class UserDetailViewModel
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? FullName { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public decimal? ApprovalLimit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public IReadOnlyList<AssignedRoleRow> Roles { get; set; } = Array.Empty<AssignedRoleRow>();

    public bool IsCurrentUser { get; set; }
    public bool IsLocked => LockedUntil is not null && LockedUntil > DateTime.UtcNow;
}

public sealed record AssignedRoleRow(
    Guid RoleId,
    string Code,
    string Name,
    bool IsSystemRole,
    DateTime CreatedAt);
