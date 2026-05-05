using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.Logging;
using WMS.Common.Multitenancy;
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

    private readonly IUserRepositoryFactory _userRepoFactory;
    private readonly IMasterConnectionFactory _masterFactory;
    private readonly ILogger<AuthService> _logger;
    private readonly int _bcryptCostFactor;

    public AuthService(
        IUserRepositoryFactory userRepoFactory,
        IMasterConnectionFactory masterFactory,
        ILogger<AuthService> logger,
        int bcryptCostFactor = 12)
    {
        if (bcryptCostFactor is < 4 or > 14)
            throw new ArgumentOutOfRangeException(
                nameof(bcryptCostFactor),
                "BCrypt cost factor must be between 4 (test) and 14 (prod ceiling).");

        _userRepoFactory = userRepoFactory;
        _masterFactory = masterFactory;
        _logger = logger;
        _bcryptCostFactor = bcryptCostFactor;
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
