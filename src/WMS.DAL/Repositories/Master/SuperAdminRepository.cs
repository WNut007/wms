using System.Data;
using Dapper;
using WMS.Common.Multitenancy;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed repo for master.SuperAdmins. Created with a fresh
// connection per call from IMasterConnectionFactory (matches the
// IUserTenantMapRepository / pre-auth-token store pattern — master DB
// access is light + non-tenant-scoped, no factory-by-id needed).
public sealed class SuperAdminRepository : ISuperAdminRepository
{
    private const string SelectColumns = @"
        SELECT Id, Email, PasswordHash, FullName, IsActive, LastLoginAt,
               FailedLoginAttempts, LockedUntil, MustChangePassword,
               Permissions, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM [master].[SuperAdmins]";

    private readonly IMasterConnectionFactory _factory;

    public SuperAdminRepository(IMasterConnectionFactory factory) =>
        _factory = factory;

    public async Task<SuperAdmin?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<SuperAdmin?>(new CommandDefinition(
            SelectColumns + " WHERE Email = @email",
            new { email },
            cancellationToken: ct));
    }

    public async Task<SuperAdmin?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<SuperAdmin?>(new CommandDefinition(
            SelectColumns + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM [master].[SuperAdmins]",
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SuperAdmin>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SuperAdmin>(new CommandDefinition(
            SelectColumns + " ORDER BY Email",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Guid> UpsertByEmailAsync(SuperAdmin entity, CancellationToken ct = default)
    {
        // MERGE keyed on Email (Unique). Used by the first-run seeder so
        // repeated config-driven runs don't multiply rows.
        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"DECLARE @result TABLE (Id UNIQUEIDENTIFIER);

              MERGE [master].[SuperAdmins] WITH (HOLDLOCK) AS target
              USING (SELECT @Email AS Email) AS source
              ON target.Email = source.Email
              WHEN MATCHED THEN
                  UPDATE SET FullName = @FullName,
                             IsActive = @IsActive,
                             UpdatedAt = SYSUTCDATETIME(),
                             UpdatedBy = @CreatedBy
              WHEN NOT MATCHED THEN
                  INSERT (Id, Email, PasswordHash, FullName, IsActive,
                          FailedLoginAttempts, MustChangePassword,
                          CreatedAt, CreatedBy)
                  VALUES (@Id, @Email, @PasswordHash, @FullName, @IsActive,
                          0, @MustChangePassword,
                          SYSUTCDATETIME(), @CreatedBy)
              OUTPUT inserted.Id INTO @result;

              SELECT TOP 1 Id FROM @result;",
            entity,
            cancellationToken: ct));
    }

    public async Task UpdateLastLoginAsync(Guid id, DateTime utcNow, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE [master].[SuperAdmins]
              SET LastLoginAt = @utcNow,
                  FailedLoginAttempts = 0,
                  LockedUntil = NULL
              WHERE Id = @id",
            new { id, utcNow },
            cancellationToken: ct));
    }

    public async Task IncrementFailedLoginAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE [master].[SuperAdmins] SET FailedLoginAttempts = FailedLoginAttempts + 1 WHERE Id = @id",
            new { id },
            cancellationToken: ct));
    }

    public async Task SetLockedUntilAsync(
        Guid id, DateTime lockedUntilUtc, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE [master].[SuperAdmins] SET LockedUntil = @lockedUntilUtc WHERE Id = @id",
            new { id, lockedUntilUtc },
            cancellationToken: ct));
    }

    public async Task ResetFailedLoginAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE [master].[SuperAdmins] SET FailedLoginAttempts = 0, LockedUntil = NULL WHERE Id = @id",
            new { id },
            cancellationToken: ct));
    }

    public async Task UpdatePasswordHashAsync(
        Guid id,
        string newPasswordHash,
        bool mustChangePassword,
        Guid? actorId,
        CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE [master].[SuperAdmins]
              SET PasswordHash = @newPasswordHash,
                  MustChangePassword = @mustChangePassword,
                  FailedLoginAttempts = 0,
                  LockedUntil = NULL,
                  UpdatedAt = SYSUTCDATETIME(),
                  UpdatedBy = @actorId
              WHERE Id = @id",
            new { id, newPasswordHash, mustChangePassword, actorId },
            cancellationToken: ct));
    }
}
