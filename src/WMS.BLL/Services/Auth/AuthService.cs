using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using WMS.BLL.Services.Security;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Security;
using WMS.Domain.Entities.Security;

namespace WMS.BLL.Services.Auth;

// Implements the auth primitives consumed by the AuthController.
//
// BCrypt cost factor is taken from the constructor (12 in prod, 4 in
// tests) — keeps the unit suite under a second while still producing
// production-shaped hashes.
//
// Lockout is read-only here: VerifyPasswordAsync returns null if a user
// is currently locked; LogLoginAttempt + IncrementFailedLogin stamp the
// audit trail. Promoting "5 failures in 15 min → set LockedUntil" is a
// later concern and lives in a higher-level service.
public sealed class AuthService : IAuthService
{
    private const int PreAuthTokenLifetimeMinutes = 5;
    private const int PreAuthTokenByteLength = 32;

    // Phase 25 — per-user lockout thresholds. 5 consecutive failures
    // within the 1-minute IP throttle window stamps LockedUntil for
    // 30 minutes. Counters reset on successful login or password
    // change (both go through UpdatePasswordHashAsync /
    // UpdateLastLoginAsync which zero FailedLoginAttempts).
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    private readonly IUserRepositoryFactory _userRepoFactory;
    private readonly IUserTenantMapRepository _userTenantMapRepo;
    private readonly IMasterConnectionFactory _masterFactory;
    private readonly IAuditLogRepositoryFactory? _auditRepoFactory;
    private readonly ILoginRateLimiter? _rateLimiter;
    private readonly ILogger<AuthService> _logger;
    private readonly int _bcryptCostFactor;

    public AuthService(
        IUserRepositoryFactory userRepoFactory,
        IUserTenantMapRepository userTenantMapRepo,
        IMasterConnectionFactory masterFactory,
        ILogger<AuthService> logger,
        int bcryptCostFactor = 12,
        IAuditLogRepositoryFactory? auditRepoFactory = null,
        ILoginRateLimiter? rateLimiter = null)
    {
        if (bcryptCostFactor is < 4 or > 14)
            throw new ArgumentOutOfRangeException(
                nameof(bcryptCostFactor),
                "BCrypt cost factor must be between 4 (test) and 14 (prod ceiling).");

        _userRepoFactory = userRepoFactory;
        _userTenantMapRepo = userTenantMapRepo;
        _masterFactory = masterFactory;
        _auditRepoFactory = auditRepoFactory;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _bcryptCostFactor = bcryptCostFactor;
    }

    public async Task<LoginResult> AuthenticateAsync(
        string email,
        string password,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        // Phase 25 — per-IP throttle BEFORE we hit the DB. Five attempts
        // per minute per IP; sixth gets 'RateLimited' back. The throttle
        // is a no-op when the IP is unknown (caller's responsibility to
        // fall back to per-user lockout via the user row).
        if (_rateLimiter is not null && !_rateLimiter.TryRegisterAttempt(ipAddress))
        {
            await LogLoginAttemptAsync(email, success: false, "RateLimited",
                ipAddress, userAgent, ct);
            return LoginResult.Failed("RateLimited");
        }

        var tenants = await _userTenantMapRepo.GetByEmailAsync(email, ct);
        if (tenants.Count == 0)
        {
            await LogLoginAttemptAsync(email, success: false, "UnknownEmail",
                ipAddress, userAgent, ct);
            // No tenant resolved → no AuditLog emission (AuditLog is
            // tenant-scoped). master.LoginAttempts is the canonical
            // record for unknown-email failures.
            return LoginResult.Failed("UnknownEmail");
        }

        // Primary tenant = first row from the repo (IsDefault DESC, Code ASC).
        // Password is assumed to be in sync across the user's tenants —
        // verifying once is sufficient for Step 1.
        var primary = tenants[0];
        var user = await VerifyPasswordAsync(primary.TenantId, email, password, ct);
        if (user is null)
        {
            await LogLoginAttemptAsync(email, success: false, "InvalidPassword",
                ipAddress, userAgent, ct);

            // Re-read the user (if they exist) so we can emit a tenant-
            // scoped LoginFailure audit + handle lockout-threshold
            // crossing. VerifyPasswordAsync stays pure (no audits, no
            // IP/UA awareness); orchestration lives here where ip+ua
            // are available.
            await EmitLoginFailureAuditAsync(
                primary.TenantId, email, ipAddress, userAgent, ct);
            return LoginResult.Failed("InvalidPassword");
        }

        var token = await CreatePreAuthTokenAsync(email, ipAddress, ct);
        await LogLoginAttemptAsync(email, success: true, null,
            ipAddress, userAgent, ct);

        // Successful login → clear IP throttle so a stray earlier mistype
        // doesn't burn the operator's quota, and emit LoginSuccess audit.
        _rateLimiter?.Clear(ipAddress);
        await AppendAuditAsync(primary.TenantId, user.Id,
            AuditEventTypes.LoginSuccess, AuditEventTypes.EntityUser, user.Id,
            ipAddress, userAgent, details: null, ct);

        return LoginResult.Succeeded(token, tenants);
    }

    public async Task<User?> VerifyPasswordAsync(
        Guid tenantId,
        string email,
        string password,
        CancellationToken ct = default)
    {
        var repo = _userRepoFactory.For(tenantId);
        var user = await repo.GetByEmailAsync(email, ct);

        if (user is null || !user.IsActive)
            return null;

        if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
            return null;

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            await repo.IncrementFailedLoginAsync(user.Id, ct);
            return null;
        }

        await repo.UpdateLastLoginAsync(user.Id, DateTime.UtcNow, ct);
        return user;
    }

    // Phase 25 — post-failure audit + lockout stamping. Called from
    // AuthenticateAsync when VerifyPasswordAsync returns null and we've
    // already resolved the user's primary tenant. Best-effort: if the
    // user row doesn't exist (race with deletion), silently skip.
    private async Task EmitLoginFailureAuditAsync(
        Guid tenantId,
        string email,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        if (_auditRepoFactory is null) return;

        var repo = _userRepoFactory.For(tenantId);
        var user = await repo.GetByEmailAsync(email, ct);
        if (user is null) return;

        // Check if we just crossed the lockout threshold. The counter
        // was incremented inside VerifyPasswordAsync's wrong-password
        // branch, so this read picks up the new value.
        var crossedThreshold =
            user.FailedLoginAttempts >= LockoutThreshold
            && (user.LockedUntil is null || user.LockedUntil <= DateTime.UtcNow);

        if (crossedThreshold)
        {
            var lockedUntil = DateTime.UtcNow + LockoutDuration;
            await repo.SetLockedUntilAsync(user.Id, lockedUntil, ct);
            await AppendAuditAsync(tenantId, user.Id,
                AuditEventTypes.AccountLockout, AuditEventTypes.EntityUser, user.Id,
                ipAddress, userAgent,
                details: JsonSerializer.Serialize(new
                {
                    lockedUntil,
                    failedAttempts = user.FailedLoginAttempts,
                    lockoutDurationMinutes = LockoutDuration.TotalMinutes,
                }), ct);
        }

        await AppendAuditAsync(tenantId, user.Id,
            AuditEventTypes.LoginFailure, AuditEventTypes.EntityUser, user.Id,
            ipAddress, userAgent,
            details: JsonSerializer.Serialize(new
            {
                reason = user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow
                    ? "AccountLocked"
                    : "InvalidPassword",
                failedAttempts = user.FailedLoginAttempts,
            }), ct);
    }

    // Phase 25 — tenant-scoped audit emit. No-op when the factory wasn't
    // injected (keeps Phase 3 tests + DI configurations working without
    // forcing every consumer to wire AuditLog). Per-tenant — audits
    // attach to the user's primary tenant DB.
    private async Task AppendAuditAsync(
        Guid tenantId,
        Guid? userId,
        string eventType,
        string? entityType,
        Guid? entityId,
        string? ipAddress,
        string? userAgent,
        string? details,
        CancellationToken ct)
    {
        if (_auditRepoFactory is null) return;
        var repo = _auditRepoFactory.For(tenantId);
        await repo.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details = details,
        }, ct);
    }

    public string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, _bcryptCostFactor);

    public async Task LogLoginAttemptAsync(
        string email,
        bool success,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        using var conn = _masterFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO master.LoginAttempts
                  (Email, IpAddress, UserAgent, Success, FailureReason, AttemptedAt)
              VALUES (@email, @ip, @ua, @success, @reason, SYSUTCDATETIME())",
            new
            {
                email,
                ip = ipAddress,
                ua = userAgent,
                success,
                reason = failureReason
            },
            cancellationToken: ct));
    }

    public async Task<string> CreatePreAuthTokenAsync(
        string email,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var token = GenerateToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(PreAuthTokenLifetimeMinutes);

        using var conn = _masterFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO master.PreAuthTokens
                  (UserEmail, Token, ExpiresAt, IpAddress)
              VALUES (@email, @token, @expiresAt, @ip)",
            new { email, token, expiresAt, ip = ipAddress },
            cancellationToken: ct));

        return token;
    }

    public async Task<PreAuthData?> ValidatePreAuthTokenAsync(
        string token,
        CancellationToken ct = default)
    {
        using var conn = _masterFactory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<PreAuthData?>(new CommandDefinition(
            @"SELECT Id, UserEmail, ExpiresAt, IpAddress
              FROM master.PreAuthTokens
              WHERE Token = @token
                AND UsedAt IS NULL
                AND ExpiresAt > SYSUTCDATETIME()",
            new { token },
            cancellationToken: ct));
        return row;
    }

    public async Task MarkPreAuthTokenUsedAsync(
        string token,
        CancellationToken ct = default)
    {
        using var conn = _masterFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE master.PreAuthTokens
              SET UsedAt = SYSUTCDATETIME()
              WHERE Token = @token",
            new { token },
            cancellationToken: ct));
    }

    // 32 bytes of cryptographic randomness, base64url-encoded → ~43
    // characters; fits well inside the Token column's 500-char budget.
    private static string GenerateToken()
    {
        Span<byte> buf = stackalloc byte[PreAuthTokenByteLength];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToBase64String(buf)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
