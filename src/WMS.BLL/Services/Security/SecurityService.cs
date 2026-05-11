using System.Text.Json;
using WMS.BLL.Services.Auth;
using WMS.DAL.Repositories.Security;
using WMS.Domain.Entities.Security;

namespace WMS.BLL.Services.Security;

// Phase 24 — orchestrates the admin write paths across User + UserRole +
// AuditLog repos. Invariants live here (last-admin guard, can't toggle
// self, system-role guards). Every write emits an AuditLog row before
// returning.
//
// No TransactionScope wrappers in v1 — each write is a single repo call
// or a tight sequence where mid-failure leaves the system in a state
// the next call can re-apply (idempotent). UserRole replacement is the
// closest to needing TX coverage (diff-then-apply); if partial-replace
// becomes a real problem, wrap it later per `feedback_transactionscope_dapper.md`.
public sealed class SecurityService : ISecurityService
{
    private const string AdminRoleCode = "ADMIN";

    private readonly IUserRepositoryFactory _userRepos;
    private readonly IUserRoleRepositoryFactory _userRoleRepos;
    private readonly IRoleRepositoryFactory _roleRepos;
    private readonly IAuditLogRepositoryFactory _auditRepos;
    private readonly IAuthService _auth;

    public SecurityService(
        IUserRepositoryFactory userRepos,
        IUserRoleRepositoryFactory userRoleRepos,
        IRoleRepositoryFactory roleRepos,
        IAuditLogRepositoryFactory auditRepos,
        IAuthService auth)
    {
        _userRepos = userRepos;
        _userRoleRepos = userRoleRepos;
        _roleRepos = roleRepos;
        _auditRepos = auditRepos;
        _auth = auth;
    }

    public async Task<Guid> CreateUserAsync(
        Guid tenantId,
        CreateUserRequest request,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var users = _userRepos.For(tenantId);
        var userRoles = _userRoleRepos.For(tenantId);
        var audit = _auditRepos.For(tenantId);

        ValidateEmail(request.Email);
        ValidatePassword(request.Password);
        if (await users.EmailExistsAsync(request.Email, exceptId: null, ct))
            throw new InvalidOperationException($"User with email '{request.Email}' already exists.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            PasswordHash = _auth.HashPassword(request.Password),
            FullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim(),
            IsActive = true,
            ApprovalLimit = request.ApprovalLimit,
            CreatedBy = actorId,
        };

        await users.InsertAsync(user, ct);

        if (request.RoleIds.Count > 0)
            await userRoles.ReplaceForUserAsync(user.Id, request.RoleIds, actorId, ct);

        await audit.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            EventType = AuditEventTypes.UserCreated,
            EntityType = AuditEventTypes.EntityUser,
            EntityId = user.Id,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details = JsonSerializer.Serialize(new
            {
                user.Email,
                user.FullName,
                RoleCount = request.RoleIds.Count,
                user.ApprovalLimit,
            }),
        }, ct);

        return user.Id;
    }

    public async Task UpdateUserAsync(
        Guid tenantId,
        UpdateUserRequest request,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var users = _userRepos.For(tenantId);
        var userRoles = _userRoleRepos.For(tenantId);
        var audit = _auditRepos.For(tenantId);

        ValidateEmail(request.Email);

        var existing = await users.GetByIdAsync(request.Id, ct)
            ?? throw new InvalidOperationException($"User '{request.Id}' not found.");

        if (!string.Equals(existing.Email, request.Email, StringComparison.OrdinalIgnoreCase)
            && await users.EmailExistsAsync(request.Email, exceptId: request.Id, ct))
        {
            throw new InvalidOperationException($"Email '{request.Email}' is already in use.");
        }

        existing.Email = request.Email.Trim();
        existing.FullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim();
        existing.ApprovalLimit = request.ApprovalLimit;
        existing.UpdatedBy = actorId;

        await users.UpdateAsync(existing, ct);

        var (added, removed) = await userRoles.ReplaceForUserAsync(
            request.Id, request.RoleIds, actorId, ct);

        await audit.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            EventType = AuditEventTypes.UserUpdated,
            EntityType = AuditEventTypes.EntityUser,
            EntityId = request.Id,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details = JsonSerializer.Serialize(new
            {
                existing.Email,
                existing.FullName,
                RolesAdded = added,
                RolesRemoved = removed,
                existing.ApprovalLimit,
            }),
        }, ct);
    }

    public async Task ToggleUserActiveAsync(
        Guid tenantId,
        Guid userId,
        bool isActive,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        if (userId == actorId && !isActive)
            throw new InvalidOperationException("You cannot deactivate your own account.");

        var users = _userRepos.For(tenantId);
        var audit = _auditRepos.For(tenantId);

        // Last-admin guard: if we're about to deactivate, count distinct
        // ACTIVE users holding the ADMIN role. Refuse if this would drop
        // it below 1.
        if (!isActive)
        {
            var existing = await users.GetByIdAsync(userId, ct)
                ?? throw new InvalidOperationException($"User '{userId}' not found.");
            if (!existing.IsActive) return;  // already inactive — no-op + no audit

            // Only check if THIS user holds ADMIN. If they don't, the
            // count's irrelevant.
            var userRoles = _userRoleRepos.For(tenantId);
            var roleIds = await userRoles.GetRoleIdsByUserAsync(userId, ct);
            var roles = _roleRepos.For(tenantId);
            var holdsAdmin = false;
            foreach (var rid in roleIds)
            {
                var role = await roles.GetByIdAsync(rid, ct);
                if (role is not null && string.Equals(role.Code, AdminRoleCode, StringComparison.Ordinal))
                {
                    holdsAdmin = true;
                    break;
                }
            }
            if (holdsAdmin)
            {
                var activeAdmins = await users.CountActiveAdminsAsync(AdminRoleCode, ct);
                if (activeAdmins <= 1)
                    throw new InvalidOperationException(
                        "Cannot deactivate the last active ADMIN. Grant ADMIN to another user first.");
            }
        }

        var changed = await users.SetActiveAsync(userId, isActive, actorId, ct);
        if (!changed) return;  // idempotent — already at target state

        await audit.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            EventType = isActive ? AuditEventTypes.UserActivated : AuditEventTypes.UserDeactivated,
            EntityType = AuditEventTypes.EntityUser,
            EntityId = userId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details = JsonSerializer.Serialize(new { isActive }),
        }, ct);
    }

    public async Task UnlockUserAsync(
        Guid tenantId,
        Guid userId,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var users = _userRepos.For(tenantId);
        var audit = _auditRepos.For(tenantId);

        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        // No-op if not locked + zero failed attempts.
        if (user.FailedLoginAttempts == 0 && user.LockedUntil is null) return;

        await users.ResetFailedLoginAsync(userId, ct);

        await audit.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            EventType = AuditEventTypes.UserUnlocked,
            EntityType = AuditEventTypes.EntityUser,
            EntityId = userId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        }, ct);
    }

    // ── Passwords ──────────────────────────────────────────────────────

    public async Task ChangePasswordAsync(
        Guid tenantId,
        Guid userId,
        string currentPassword,
        string newPassword,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        PasswordPolicy.ThrowIfInvalid(newPassword);
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("New password must differ from current password.");

        var users = _userRepos.For(tenantId);
        var audit = _auditRepos.For(tenantId);

        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found.");
        if (!user.IsActive)
            throw new InvalidOperationException("Account is inactive.");

        // Verify @currentPassword against stored hash. BCrypt.Verify is
        // the same primitive AuthService uses at login.
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            throw new InvalidOperationException("Current password is incorrect.");

        var newHash = _auth.HashPassword(newPassword);
        await users.UpdatePasswordHashAsync(userId, newHash, userId, ct);

        await audit.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,    // actor IS the target on self-change
            EventType = AuditEventTypes.PasswordChangedSelf,
            EntityType = AuditEventTypes.EntityUser,
            EntityId = userId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        }, ct);
    }

    public async Task ResetPasswordAsync(
        Guid tenantId,
        Guid targetUserId,
        string newPassword,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        // Critical safeguard (D2): admin cannot reset their own password
        // through the admin endpoint — forces use of the self-change flow
        // (which requires current-password verification) so the admin
        // path can't be used to bypass that check on themselves.
        if (targetUserId == actorId)
            throw new InvalidOperationException(
                "Use /Account/ChangePassword to change your own password — it requires verifying your current password.");

        PasswordPolicy.ThrowIfInvalid(newPassword);

        var users = _userRepos.For(tenantId);
        var audit = _auditRepos.For(tenantId);

        var user = await users.GetByIdAsync(targetUserId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var newHash = _auth.HashPassword(newPassword);
        await users.UpdatePasswordHashAsync(targetUserId, newHash, actorId, ct);

        await audit.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            EventType = AuditEventTypes.PasswordResetAdmin,
            EntityType = AuditEventTypes.EntityUser,
            EntityId = targetUserId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        }, ct);
    }

    public async Task SetPermissionAsync(
        Guid tenantId,
        SetPermissionRequest request,
        Guid actorId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var roles = _roleRepos.For(tenantId);
        var audit = _auditRepos.For(tenantId);

        var role = await roles.GetByIdAsync(request.RoleId, ct)
            ?? throw new InvalidOperationException($"Role '{request.RoleId}' not found.");

        // System roles are immutable per Migration_035 comment. Tenant
        // admins can't accidentally remove a permission from ADMIN /
        // PICKER / PACKER / MANAGER and lock themselves out of the
        // baseline matrix.
        if (role.IsSystemRole)
            throw new InvalidOperationException(
                $"Role '{role.Code}' is a system role — permissions are baseline and not editable.");

        await roles.UpsertPermissionAsync(
            request.RoleId,
            request.FunctionId,
            request.CanView, request.CanAdd, request.CanEdit, request.CanDelete, request.CanApprove,
            actorId, ct);

        await audit.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            EventType = AuditEventTypes.RolePermissionChanged,
            EntityType = AuditEventTypes.EntityRole,
            EntityId = request.RoleId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details = JsonSerializer.Serialize(new
            {
                request.FunctionId,
                request.CanView,
                request.CanAdd,
                request.CanEdit,
                request.CanDelete,
                request.CanApprove,
            }),
        }, ct);
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (email.Length > 100)
            throw new ArgumentException("Email must be 100 characters or fewer.", nameof(email));
        if (!email.Contains('@'))
            throw new ArgumentException("Email must contain '@'.", nameof(email));
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.", nameof(password));
        if (password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.", nameof(password));
    }
}
