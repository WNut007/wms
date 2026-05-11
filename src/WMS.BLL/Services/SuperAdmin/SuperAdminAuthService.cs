using System.Text.Json;
using WMS.BLL.Services.Auth;
using WMS.BLL.Services.Security;
using WMS.DAL.Repositories.Master;
using SuperAdminEntity = WMS.Domain.Entities.Master.SuperAdmin;

namespace WMS.BLL.Services.SuperAdmin;

public sealed class SuperAdminAuthService : ISuperAdminAuthService
{
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    private readonly ISuperAdminRepository _repo;
    private readonly ISystemAuditLogRepository _audit;
    private readonly IAuthService _auth;
    private readonly ILoginRateLimiter _rateLimiter;

    public SuperAdminAuthService(
        ISuperAdminRepository repo,
        ISystemAuditLogRepository audit,
        IAuthService auth,
        ILoginRateLimiter rateLimiter)
    {
        _repo = repo;
        _audit = audit;
        _auth = auth;
        _rateLimiter = rateLimiter;
    }

    public async Task<SuperAdminLoginResult> AuthenticateAsync(
        string email,
        string password,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        // Per-IP throttle (reuses Phase 25's singleton rate limiter —
        // SAME counter as tenant login, by design: 5 attempts across
        // ANY /Login surface from one IP).
        if (!_rateLimiter.TryRegisterAttempt(ipAddress))
        {
            await EmitAsync(SystemAuditEventTypes.SuperAdminLoginFailure,
                SystemAuditEventTypes.SeverityWarning,
                userId: null, userEmail: email, entityId: null,
                ipAddress, userAgent,
                details: "{\"reason\":\"RateLimited\"}", ct);
            return SuperAdminLoginResult.Failed("RateLimited");
        }

        var admin = await _repo.GetByEmailAsync(email, ct);
        if (admin is null)
        {
            await EmitAsync(SystemAuditEventTypes.SuperAdminLoginFailure,
                SystemAuditEventTypes.SeverityWarning,
                userId: null, userEmail: email, entityId: null,
                ipAddress, userAgent,
                details: "{\"reason\":\"UnknownEmail\"}", ct);
            return SuperAdminLoginResult.Failed("UnknownEmail");
        }

        if (!admin.IsActive)
        {
            await EmitFailureAsync(admin, ipAddress, userAgent, "AccountInactive", failedAttempts: null, ct);
            return SuperAdminLoginResult.Failed("AccountInactive");
        }

        if (admin.LockedUntil is not null && admin.LockedUntil > DateTime.UtcNow)
        {
            await EmitFailureAsync(admin, ipAddress, userAgent, "AccountLocked", failedAttempts: null, ct);
            return SuperAdminLoginResult.Failed("AccountLocked");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
        {
            await _repo.IncrementFailedLoginAsync(admin.Id, ct);
            var refreshed = await _repo.GetByIdAsync(admin.Id, ct);

            if (refreshed is not null
                && refreshed.FailedLoginAttempts >= LockoutThreshold
                && (refreshed.LockedUntil is null || refreshed.LockedUntil <= DateTime.UtcNow))
            {
                var lockedUntil = DateTime.UtcNow + LockoutDuration;
                await _repo.SetLockedUntilAsync(refreshed.Id, lockedUntil, ct);
                await EmitAsync(SystemAuditEventTypes.SuperAdminLockout,
                    SystemAuditEventTypes.SeverityError,
                    userId: admin.Id, userEmail: admin.Email, entityId: admin.Id,
                    ipAddress, userAgent,
                    details: JsonSerializer.Serialize(new
                    {
                        lockedUntil,
                        failedAttempts = refreshed.FailedLoginAttempts,
                        lockoutDurationMinutes = LockoutDuration.TotalMinutes,
                    }), ct);
            }

            await EmitFailureAsync(admin, ipAddress, userAgent, "InvalidPassword",
                refreshed?.FailedLoginAttempts ?? admin.FailedLoginAttempts + 1, ct);
            return SuperAdminLoginResult.Failed("InvalidPassword");
        }

        // Successful auth — clear rate limit + audit + refresh last-login.
        _rateLimiter.Clear(ipAddress);
        await _repo.UpdateLastLoginAsync(admin.Id, DateTime.UtcNow, ct);
        await EmitAsync(SystemAuditEventTypes.SuperAdminLoginSuccess,
            SystemAuditEventTypes.SeverityInfo,
            userId: admin.Id, userEmail: admin.Email, entityId: admin.Id,
            ipAddress, userAgent, details: null, ct);

        return SuperAdminLoginResult.Succeeded(admin);
    }

    public async Task ChangePasswordAsync(
        Guid superAdminId,
        string currentPassword,
        string newPassword,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        PasswordPolicy.ThrowIfInvalid(newPassword);
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("New password must differ from current password.");

        var admin = await _repo.GetByIdAsync(superAdminId, ct)
            ?? throw new InvalidOperationException("SuperAdmin not found.");
        if (!admin.IsActive)
            throw new InvalidOperationException("Account is inactive.");
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, admin.PasswordHash))
            throw new InvalidOperationException("Current password is incorrect.");

        var newHash = _auth.HashPassword(newPassword);
        await _repo.UpdatePasswordHashAsync(admin.Id, newHash, mustChangePassword: false, admin.Id, ct);

        await EmitAsync(SystemAuditEventTypes.SuperAdminPasswordChange,
            SystemAuditEventTypes.SeverityInfo,
            userId: admin.Id, userEmail: admin.Email, entityId: admin.Id,
            ipAddress, userAgent, details: null, ct);
    }

    private Task EmitFailureAsync(
        SuperAdminEntity admin, string? ip, string? ua, string reason, int? failedAttempts = null,
        CancellationToken ct = default) =>
        EmitAsync(SystemAuditEventTypes.SuperAdminLoginFailure,
            SystemAuditEventTypes.SeverityWarning,
            userId: admin.Id, userEmail: admin.Email, entityId: admin.Id,
            ip, ua,
            details: JsonSerializer.Serialize(new { reason, failedAttempts }), ct);

    private Task EmitAsync(
        string eventType, string severity,
        Guid? userId, string? userEmail, Guid? entityId,
        string? ipAddress, string? userAgent, string? details,
        CancellationToken ct) =>
        _audit.AppendAsync(new SystemAuditLogEntry(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Severity: severity,
            UserId: userId,
            UserEmail: userEmail,
            TenantId: null,    // SuperAdmin events are cross-tenant
            EntityType: SystemAuditEventTypes.EntitySuperAdmin,
            EntityId: entityId,
            Details: details,
            IpAddress: ipAddress,
            Timestamp: DateTime.UtcNow),
            ct);
}
