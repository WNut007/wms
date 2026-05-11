using SuperAdminEntity = WMS.Domain.Entities.Master.SuperAdmin;

namespace WMS.BLL.Services.SuperAdmin;

// Phase 27 — authentication for /SuperAdmin/. Distinct from
// tenant-side IAuthService: no PreAuthToken / SelectTenant flow
// (SuperAdmins don't belong to a tenant), simpler 1-step login.
//
// Reuses Phase 25's LoginRateLimiter (singleton, in-process) +
// per-user lockout (5 fails in 60s window → 30-min LockedUntil).
// All events emit to master.SystemAuditLog.
public interface ISuperAdminAuthService
{
    Task<SuperAdminLoginResult> AuthenticateAsync(
        string email,
        string password,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    // Force-change-password flow. Verifies current password, applies
    // new (with PasswordPolicy), clears MustChangePassword. Audits
    // SuperAdminPasswordChange.
    Task ChangePasswordAsync(
        Guid superAdminId,
        string currentPassword,
        string newPassword,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}

// Failure reasons: 'RateLimited' / 'UnknownEmail' / 'InvalidPassword' /
// 'AccountLocked' / 'AccountInactive'. Operator-facing message
// collapses UnknownEmail + InvalidPassword to a shared "Invalid email
// or password" (avoid email enumeration); only RateLimited gets a
// distinct message.
public sealed record SuperAdminLoginResult(
    bool Success,
    string? FailureReason,
    SuperAdminEntity? Admin)
{
    public static SuperAdminLoginResult Failed(string reason) =>
        new(false, reason, null);

    public static SuperAdminLoginResult Succeeded(SuperAdminEntity admin) =>
        new(true, null, admin);
}
