using System.Data;
using Dapper;
using WMS.DAL.Common;
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
    private const string SelectColumns = @"
        SELECT Id, Email, PasswordHash, FullName, IsActive, LastLoginAt,
               FailedLoginAttempts, LockedUntil, ApprovalLimit,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM security.Users";

    private readonly IDbConnection _connection;

    public UserRepository(IDbConnection connection) => _connection = connection;

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<User?>(new CommandDefinition(
            SelectColumns + " WHERE Email = @email",
            new { email },
            cancellationToken: ct));

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<User?>(new CommandDefinition(
            SelectColumns + " WHERE Id = @id",
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

    // ── Phase 24 admin CRUD ────────────────────────────────────────────

    public async Task<PagedResult<UserListRow>> GetPagedAsync(
        UserFilter filter,
        CancellationToken ct = default)
    {
        var col = UserSortMapper.ResolveColumn(filter.SortBy);
        var dir = filter.SortDesc ? "DESC" : "ASC";
        var offset = (filter.Page - 1) * filter.PageSize;

        // 'locked' status = LockedUntil > now. 'active'/'inactive' map
        // directly to IsActive. Status filter applied at WHERE level so
        // pagination + chip counts agree.
        var sql = $@"
WITH UserRoleAgg AS (
    SELECT ur.UserId,
           STRING_AGG(r.Code, ',') WITHIN GROUP (ORDER BY r.Code) AS RoleCodes
    FROM security.UserRoles ur
    INNER JOIN security.Roles r ON r.Id = ur.RoleId
    WHERE r.IsActive = 1
    GROUP BY ur.UserId
)
SELECT u.Id, u.Email, u.FullName, u.IsActive, u.LastLoginAt,
       u.FailedLoginAttempts, u.LockedUntil,
       ura.RoleCodes,
       u.CreatedAt
FROM security.Users u
LEFT JOIN UserRoleAgg ura ON ura.UserId = u.Id
WHERE
    (@search IS NULL
        OR u.Email LIKE '%' + @search + '%'
        OR u.FullName LIKE '%' + @search + '%')
AND (@status IS NULL
        OR (@status = 'active'   AND u.IsActive = 1)
        OR (@status = 'inactive' AND u.IsActive = 0)
        OR (@status = 'locked'   AND u.LockedUntil IS NOT NULL AND u.LockedUntil > SYSUTCDATETIME()))
AND (@roleCode IS NULL
        OR EXISTS (SELECT 1 FROM security.UserRoles urx
                   INNER JOIN security.Roles rx ON rx.Id = urx.RoleId
                   WHERE urx.UserId = u.Id AND rx.Code = @roleCode))
ORDER BY {col} {dir}
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

SELECT COUNT(*)
FROM security.Users u
WHERE
    (@search IS NULL
        OR u.Email LIKE '%' + @search + '%'
        OR u.FullName LIKE '%' + @search + '%')
AND (@status IS NULL
        OR (@status = 'active'   AND u.IsActive = 1)
        OR (@status = 'inactive' AND u.IsActive = 0)
        OR (@status = 'locked'   AND u.LockedUntil IS NOT NULL AND u.LockedUntil > SYSUTCDATETIME()))
AND (@roleCode IS NULL
        OR EXISTS (SELECT 1 FROM security.UserRoles urx
                   INNER JOIN security.Roles rx ON rx.Id = urx.RoleId
                   WHERE urx.UserId = u.Id AND rx.Code = @roleCode));";

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new { search = filter.Search, status = filter.Status, roleCode = filter.RoleCode,
                  offset, pageSize = filter.PageSize },
            cancellationToken: ct));

        var rows = (await multi.ReadAsync<UserListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();
        return new PagedResult<UserListRow>
        {
            Items = rows,
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)filter.PageSize),
        };
    }

    public async Task<UserStatusCounts> GetStatusCountsAsync(
        UserFilter filter,
        CancellationToken ct = default)
    {
        // Counts ignore Status filter (so inactive chips still display
        // totals) but DO respect Search + RoleCode.
        const string sql = @"
SELECT
    SUM(1) AS [All],
    SUM(CASE WHEN u.IsActive = 1 THEN 1 ELSE 0 END) AS Active,
    SUM(CASE WHEN u.IsActive = 0 THEN 1 ELSE 0 END) AS Inactive,
    SUM(CASE WHEN u.LockedUntil IS NOT NULL AND u.LockedUntil > SYSUTCDATETIME() THEN 1 ELSE 0 END) AS Locked
FROM security.Users u
WHERE
    (@search IS NULL
        OR u.Email LIKE '%' + @search + '%'
        OR u.FullName LIKE '%' + @search + '%')
AND (@roleCode IS NULL
        OR EXISTS (SELECT 1 FROM security.UserRoles urx
                   INNER JOIN security.Roles rx ON rx.Id = urx.RoleId
                   WHERE urx.UserId = u.Id AND rx.Code = @roleCode));";

        var result = await _connection.QuerySingleOrDefaultAsync<UserStatusCounts>(
            new CommandDefinition(
                sql,
                new { search = filter.Search, roleCode = filter.RoleCode },
                cancellationToken: ct));
        return result ?? new UserStatusCounts(0, 0, 0, 0);
    }

    public async Task<bool> EmailExistsAsync(
        string email,
        Guid? exceptId,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT CAST(CASE WHEN EXISTS (
    SELECT 1 FROM security.Users
    WHERE Email = @email
      AND (@exceptId IS NULL OR Id <> @exceptId)
) THEN 1 ELSE 0 END AS BIT);";
        return await _connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql, new { email, exceptId }, cancellationToken: ct));
    }

    public Task InsertAsync(User user, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO security.Users
                (Id, Email, PasswordHash, FullName, IsActive,
                 FailedLoginAttempts, ApprovalLimit,
                 CreatedAt, CreatedBy)
              VALUES
                (@Id, @Email, @PasswordHash, @FullName, @IsActive,
                 0, @ApprovalLimit,
                 SYSUTCDATETIME(), @CreatedBy)",
            user,
            cancellationToken: ct));

    public Task UpdateAsync(User user, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE security.Users
              SET Email = @Email,
                  FullName = @FullName,
                  ApprovalLimit = @ApprovalLimit,
                  UpdatedAt = SYSUTCDATETIME(),
                  UpdatedBy = @UpdatedBy
              WHERE Id = @Id",
            user,
            cancellationToken: ct));

    public async Task<bool> SetActiveAsync(
        Guid userId,
        bool isActive,
        Guid? actorId,
        CancellationToken ct = default)
    {
        // Idempotent: WHERE clause skips already-at-target rows; rowsAffected
        // distinguishes "actually changed" from "no-op".
        var rowsAffected = await _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE security.Users
              SET IsActive = @isActive,
                  UpdatedAt = SYSUTCDATETIME(),
                  UpdatedBy = @actorId
              WHERE Id = @userId AND IsActive <> @isActive",
            new { userId, isActive, actorId },
            cancellationToken: ct));
        return rowsAffected > 0;
    }

    public Task<int> CountActiveAdminsAsync(string adminRoleCode, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(DISTINCT u.Id)
              FROM security.Users u
              INNER JOIN security.UserRoles ur ON ur.UserId = u.Id
              INNER JOIN security.Roles r ON r.Id = ur.RoleId
              WHERE u.IsActive = 1
                AND r.Code = @adminRoleCode",
            new { adminRoleCode },
            cancellationToken: ct));

    public Task UpdatePasswordHashAsync(
        Guid userId,
        string newPasswordHash,
        Guid? actorId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE security.Users
              SET PasswordHash = @newPasswordHash,
                  FailedLoginAttempts = 0,
                  LockedUntil = NULL,
                  UpdatedAt = SYSUTCDATETIME(),
                  UpdatedBy = @actorId
              WHERE Id = @userId",
            new { userId, newPasswordHash, actorId },
            cancellationToken: ct));

    public Task SetLockedUntilAsync(
        Guid userId,
        DateTime lockedUntilUtc,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            "UPDATE security.Users SET LockedUntil = @lockedUntilUtc WHERE Id = @userId",
            new { userId, lockedUntilUtc },
            cancellationToken: ct));
}
