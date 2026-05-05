namespace WMS.Domain.Entities.Security;

// Maps to security.Users in the tenant DB. PasswordHash is BCrypt
// modular-crypt format (~60 chars; column reserved at 500 for future
// algorithms). FailedLoginAttempts + LockedUntil drive lockout policy
// applied by AuthService — repository just reads/writes the values.
public sealed class User : BaseEntity
{
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public decimal? ApprovalLimit { get; set; }
}
