using System.Data;
using Dapper;
using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

// Dapper-based repo against security.Users. Bound to a single tenant
// DB connection in its ctor — IUserRepositoryFactory creates an
// instance per tenantId using ITenantConnectionFactory.
//
// Email lookups are case-insensitive at the SQL level; the table's
// AnsiString collation does the work.
internal sealed class UserRepository : IUserRepository
{
    private readonly IDbConnection _connection;

    public UserRepository(IDbConnection connection) => _connection = connection;

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<User?>(new CommandDefinition(
            @"SELECT Id, Email, PasswordHash, FullName, IsActive, LastLoginAt,
                     FailedLoginAttempts, LockedUntil, ApprovalLimit,
                     CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
              FROM security.Users
              WHERE Email = @email",
            new { email },
            cancellationToken: ct));

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<User?>(new CommandDefinition(
            @"SELECT Id, Email, PasswordHash, FullName, IsActive, LastLoginAt,
                     FailedLoginAttempts, LockedUntil, ApprovalLimit,
                     CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
              FROM security.Users
              WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task UpdateLastLoginAsync(Guid userId, DateTime utcNow, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE security.Users
              SET LastLoginAt = @utcNow,
                  FailedLoginAttempts = 0,
                  LockedUntil = NULL
              WHERE Id = @userId",
            new { userId, utcNow },
            cancellationToken: ct));

    public Task IncrementFailedLoginAsync(Guid userId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE security.Users
              SET FailedLoginAttempts = FailedLoginAttempts + 1
              WHERE Id = @userId",
            new { userId },
            cancellationToken: ct));

    public Task ResetFailedLoginAsync(Guid userId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE security.Users
              SET FailedLoginAttempts = 0,
                  LockedUntil = NULL
              WHERE Id = @userId",
            new { userId },
            cancellationToken: ct));
}
