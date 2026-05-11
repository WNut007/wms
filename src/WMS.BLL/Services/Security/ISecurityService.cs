namespace WMS.BLL.Services.Security;

// Phase 24 — admin-facing user + role operations with built-in
// invariants (last-admin guard) + audit log emission.
//
// All operations are tenant-scoped via the (tenantId, ...) signature —
// service resolves repos via factories.
public interface ISecurityService
{
    // ── Users ──────────────────────────────────────────────────────────

    Task<Guid> CreateUserAsync(
        Guid tenantId,
        CreateUserRequest request,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    Task UpdateUserAsync(
        Guid tenantId,
        UpdateUserRequest request,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    // Activates / deactivates. Refuses to deactivate the last ACTIVE
    // user with the ADMIN role (lockout-prevention invariant). Refuses
    // to deactivate yourself.
    Task ToggleUserActiveAsync(
        Guid tenantId,
        Guid userId,
        bool isActive,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    // Clears FailedLoginAttempts + LockedUntil. No-op if user is already
    // unlocked (idempotent).
    Task UnlockUserAsync(
        Guid tenantId,
        Guid userId,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    // ── Passwords (Phase 25) ───────────────────────────────────────────

    // Self-service password change. Verifies @currentPassword against
    // the user's stored hash before applying. Enforces PasswordPolicy
    // on the new value. New hash CANNOT equal current (would no-op the
    // change). Successful change clears FailedLoginAttempts + LockedUntil
    // (same shape as a successful login). Emits PasswordChangedSelf.
    Task ChangePasswordAsync(
        Guid tenantId,
        Guid userId,
        string currentPassword,
        string newPassword,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    // Admin force-reset. Refuses if @actorId == @targetUserId — the
    // admin should use ChangePasswordAsync for their own account (so
    // their current password is verified). Enforces PasswordPolicy.
    // Successful reset clears FailedLoginAttempts + LockedUntil. Emits
    // PasswordResetAdmin.
    Task ResetPasswordAsync(
        Guid tenantId,
        Guid targetUserId,
        string newPassword,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    // ── Roles ──────────────────────────────────────────────────────────

    // Set one permission cell. Audited per (Role, Function) change.
    Task SetPermissionAsync(
        Guid tenantId,
        SetPermissionRequest request,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}

// Request shapes — flat records so the controller can construct
// without a project-wide ViewModel dependency.
public sealed record CreateUserRequest(
    string Email,
    string Password,
    string? FullName,
    decimal? ApprovalLimit,
    IReadOnlyList<Guid> RoleIds);

public sealed record UpdateUserRequest(
    Guid Id,
    string Email,
    string? FullName,
    decimal? ApprovalLimit,
    IReadOnlyList<Guid> RoleIds);

public sealed record SetPermissionRequest(
    Guid RoleId,
    Guid FunctionId,
    bool CanView,
    bool CanAdd,
    bool CanEdit,
    bool CanDelete,
    bool CanApprove);
