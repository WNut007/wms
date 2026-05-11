using System.Data;
using System.Text.Json;
using Dapper;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WMS.BLL.Services.Auth;
using WMS.BLL.Services.SuperAdmin;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Security;

namespace WMS.Web.Services.SuperAdmin;

// Phase 27 — orchestrates the new-tenant provisioning chain. ~8 steps,
// each with rollback on failure of the next. Tries hard to leave the
// system in a consistent state: if migrations fail, the DB is dropped
// + the master.Tenants row deleted.
public sealed class TenantProvisioningService : ITenantProvisioningService
{
    private const string TenantTemplateKey = "TenantTemplate";

    private readonly IMasterConnectionFactory _masterFactory;
    private readonly IConfiguration _config;
    private readonly IAuthService _auth;
    private readonly ISystemAuditLogRepository _audit;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        IMasterConnectionFactory masterFactory,
        IConfiguration config,
        IAuthService auth,
        ISystemAuditLogRepository audit,
        ILogger<TenantProvisioningService> logger)
    {
        _masterFactory = masterFactory;
        _config = config;
        _auth = auth;
        _audit = audit;
        _logger = logger;
    }

    public async Task<TenantProvisioningResult> CreateTenantAsync(
        string code,
        string name,
        string adminEmail,
        string? adminFullName,
        Guid actorSuperAdminId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        ValidateCode(code);
        ValidateEmail(adminEmail);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        name = name.Trim();
        code = code.Trim().ToUpperInvariant();
        adminEmail = adminEmail.Trim().ToLowerInvariant();

        // Slug DB name: WMS_Tenant_<CODE> (uppercase, ASCII letters + digits only).
        var dbName = $"WMS_Tenant_{code}";

        await EnsureCodeAvailableAsync(code, dbName, ct);

        var tenantId = Guid.NewGuid();
        var tempPassword = TempPasswordGenerator.Generate();

        // Track which steps have been completed so we can selectively
        // roll back. Each step throws → catch + rollback in reverse.
        var step = "init";
        try
        {
            // Step 1 — insert master.Tenants row.
            step = "insert master.Tenants";
            await InsertTenantRowAsync(tenantId, code, name, dbName, actorSuperAdminId, ct);

            // Step 2 — CREATE DATABASE.
            step = "CREATE DATABASE";
            await CreateDatabaseAsync(dbName, ct);

            // Step 3 — run Tenant migrations against the new DB.
            step = "apply tenant migrations";
            await ApplyTenantMigrationsAsync(dbName);

            // Step 4 — seed bootstrap ADMIN user + role + map.
            step = "seed bootstrap ADMIN";
            await SeedBootstrapAdminAsync(tenantId, dbName, adminEmail, adminFullName, tempPassword, ct);

            // Step 5 — audit.
            await _audit.AppendAsync(new SystemAuditLogEntry(
                Id: Guid.NewGuid(),
                EventType: SystemAuditEventTypes.TenantCreated,
                Severity: SystemAuditEventTypes.SeverityInfo,
                UserId: actorSuperAdminId,
                UserEmail: null,
                TenantId: tenantId,
                EntityType: SystemAuditEventTypes.EntityTenant,
                EntityId: tenantId,
                Details: JsonSerializer.Serialize(new { code, name, dbName, adminEmail }),
                IpAddress: ipAddress,
                Timestamp: DateTime.UtcNow), ct);

            _logger.LogInformation("Tenant '{Code}' ({TenantId}) provisioned by SuperAdmin {Actor}.",
                code, tenantId, actorSuperAdminId);

            return new TenantProvisioningResult(
                TenantId: tenantId,
                Code: code,
                Name: name,
                DatabaseName: dbName,
                AdminEmail: adminEmail,
                AdminTempPassword: tempPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Tenant provisioning failed at step '{Step}' for code '{Code}'. Rolling back.",
                step, code);
            await SafeRollbackAsync(tenantId, dbName, completedThrough: step, ct);
            throw new InvalidOperationException(
                $"Tenant provisioning failed at step '{step}': {ex.Message}", ex);
        }
    }

    public async Task SuspendAsync(Guid tenantId, string reason, Guid actorSuperAdminId,
        string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 3)
            throw new ArgumentException("Suspension reason is required (3+ chars).", nameof(reason));

        await UpdateTenantStatusAsync(tenantId, fromStatus: "Active", toStatus: "Suspended", actorSuperAdminId, ct);

        await _audit.AppendAsync(new SystemAuditLogEntry(
            Id: Guid.NewGuid(),
            EventType: SystemAuditEventTypes.TenantSuspended,
            Severity: SystemAuditEventTypes.SeverityWarning,
            UserId: actorSuperAdminId, UserEmail: null,
            TenantId: tenantId, EntityType: SystemAuditEventTypes.EntityTenant, EntityId: tenantId,
            Details: JsonSerializer.Serialize(new { reason }),
            IpAddress: ipAddress, Timestamp: DateTime.UtcNow), ct);
    }

    public async Task ReactivateAsync(Guid tenantId, Guid actorSuperAdminId,
        string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        await UpdateTenantStatusAsync(tenantId, fromStatus: "Suspended", toStatus: "Active", actorSuperAdminId, ct);

        await _audit.AppendAsync(new SystemAuditLogEntry(
            Id: Guid.NewGuid(),
            EventType: SystemAuditEventTypes.TenantReactivated,
            Severity: SystemAuditEventTypes.SeverityInfo,
            UserId: actorSuperAdminId, UserEmail: null,
            TenantId: tenantId, EntityType: SystemAuditEventTypes.EntityTenant, EntityId: tenantId,
            Details: null,
            IpAddress: ipAddress, Timestamp: DateTime.UtcNow), ct);
    }

    public async Task<string> ResetTenantAdminPasswordAsync(
        Guid tenantId, Guid actorSuperAdminId,
        string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        // Resolve tenant DB name from master.Tenants.
        using var masterConn = _masterFactory.CreateConnection();
        var dbName = await masterConn.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition("SELECT DatabaseName FROM [master].[Tenants] WHERE Id = @tenantId",
                new { tenantId }, cancellationToken: ct))
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");

        var tempPassword = TempPasswordGenerator.Generate();
        var newHash = _auth.HashPassword(tempPassword);

        // Update FIRST active ADMIN user in tenant DB (ADMIN by role code).
        var tenantConn = BuildTenantConnection(dbName);
        using (var conn = new SqlConnection(tenantConn))
        {
            await conn.OpenAsync(ct);
            var rowsAffected = await conn.ExecuteAsync(new CommandDefinition(@"
UPDATE u
SET PasswordHash = @newHash,
    MustChangePassword = 1,
    FailedLoginAttempts = 0,
    LockedUntil = NULL,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = NULL
FROM security.Users u
WHERE u.Id = (
    SELECT TOP 1 u2.Id
    FROM security.Users u2
    INNER JOIN security.UserRoles ur ON ur.UserId = u2.Id
    INNER JOIN security.Roles r ON r.Id = ur.RoleId
    WHERE r.Code = 'ADMIN' AND u2.IsActive = 1
    ORDER BY u2.CreatedAt ASC);",
                new { newHash }, cancellationToken: ct));

            if (rowsAffected == 0)
                throw new InvalidOperationException(
                    $"No active ADMIN user found in tenant DB '{dbName}'.");
        }

        await _audit.AppendAsync(new SystemAuditLogEntry(
            Id: Guid.NewGuid(),
            EventType: SystemAuditEventTypes.TenantAdminPasswordReset,
            Severity: SystemAuditEventTypes.SeverityWarning,
            UserId: actorSuperAdminId, UserEmail: null,
            TenantId: tenantId, EntityType: SystemAuditEventTypes.EntityTenant, EntityId: tenantId,
            Details: null,
            IpAddress: ipAddress, Timestamp: DateTime.UtcNow), ct);

        return tempPassword;
    }

    // ── Internals ──────────────────────────────────────────────────────

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        if (code.Length is < 2 or > 20)
            throw new ArgumentException("Code must be 2-20 characters.", nameof(code));
        foreach (var c in code)
        {
            if (!char.IsLetterOrDigit(c))
                throw new ArgumentException(
                    "Code must be alphanumeric only (no spaces, hyphens, etc.). Used in DB name.",
                    nameof(code));
        }
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 100)
            throw new ArgumentException("Valid admin email required.", nameof(email));
    }

    private async Task EnsureCodeAvailableAsync(string code, string dbName, CancellationToken ct)
    {
        using var conn = _masterFactory.CreateConnection();
        var existingByCode = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM [master].[Tenants] WHERE Code = @code",
            new { code }, cancellationToken: ct));
        if (existingByCode > 0)
            throw new InvalidOperationException($"Tenant code '{code}' already exists.");

        // Also verify the DB name isn't already in use at the SQL Server
        // instance level — sys.databases catches name collisions before
        // CREATE DATABASE fails noisily.
        var dbExists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sys.databases WHERE name = @dbName",
            new { dbName }, cancellationToken: ct));
        if (dbExists > 0)
            throw new InvalidOperationException(
                $"Database '{dbName}' already exists on the SQL Server instance. " +
                "Pick a different tenant code or drop the stray DB first.");
    }

    private async Task InsertTenantRowAsync(
        Guid tenantId, string code, string name, string dbName,
        Guid actorSuperAdminId, CancellationToken ct)
    {
        using var conn = _masterFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO [master].[Tenants]
    (Id, Code, Name, DatabaseName, Status, CreatedAt, CreatedBy)
VALUES
    (@tenantId, @code, @name, @dbName, 'Active', SYSUTCDATETIME(), @actorSuperAdminId);",
            new { tenantId, code, name, dbName, actorSuperAdminId }, cancellationToken: ct));
    }

    private async Task CreateDatabaseAsync(string dbName, CancellationToken ct)
    {
        // CREATE DATABASE can't run inside a transaction; uses the master
        // DB connection. Bracket-escape the DB name to prevent injection
        // — Validate guarantees alphanumeric only, but defense in depth.
        if (dbName.Any(c => c == '[' || c == ']'))
            throw new InvalidOperationException($"Invalid DB name (contains brackets): {dbName}");

        using var conn = _masterFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            $"CREATE DATABASE [{dbName}];",
            commandTimeout: 60,
            cancellationToken: ct));
    }

    private Task ApplyTenantMigrationsAsync(string dbName)
    {
        // Build an isolated DI scope with FluentMigrator pointed at the
        // new tenant DB. Tag='Tenant' so only Tenant-tagged migrations
        // run (Master-tagged stay in master.* DB).
        var tenantConn = BuildTenantConnection(dbName);

        var services = new ServiceCollection()
            .AddLogging(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSqlServer()
                .WithGlobalConnectionString(tenantConn)
                // Reference the WMS.Migrate assembly explicitly — migrations
                // live there.
                .ScanIn(typeof(WMS.Migrate.Migrations.Tenant.Migration_20260504_034_CreateUsersTable).Assembly)
                .For.Migrations())
            .Configure<RunnerOptions>(opts =>
            {
                opts.Tags = new[] { "Tenant" };
            })
            .BuildServiceProvider(validateScopes: false);

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        return Task.CompletedTask;
    }

    private async Task SeedBootstrapAdminAsync(
        Guid tenantId,
        string dbName,
        string adminEmail,
        string? adminFullName,
        string tempPassword,
        CancellationToken ct)
    {
        var tenantConn = BuildTenantConnection(dbName);
        using var conn = new SqlConnection(tenantConn);
        await conn.OpenAsync(ct);

        var userId = Guid.NewGuid();
        var passwordHash = _auth.HashPassword(tempPassword);

        // Insert User with MustChangePassword=true so first login forces
        // a rotation. ApprovalLimit null — caller can adjust later.
        await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO security.Users
    (Id, Email, PasswordHash, FullName, IsActive,
     FailedLoginAttempts, MustChangePassword, CreatedAt)
VALUES
    (@userId, @email, @passwordHash, @fullName, 1,
     0, 1, SYSUTCDATETIME());",
            new { userId, email = adminEmail, passwordHash, fullName = adminFullName },
            cancellationToken: ct));

        // Grant ADMIN role (seeded by Migration_040 in every tenant DB).
        await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO security.UserRoles
    (Id, UserId, RoleId, AssignedBy, CreatedAt)
SELECT NEWID(), @userId, r.Id, @userId, SYSUTCDATETIME()
FROM security.Roles r
WHERE r.Code = 'ADMIN';",
            new { userId },
            cancellationToken: ct));

        // Map the admin email to this tenant in master.UserTenantMap
        // (IsDefault=1 so 3-step login resolves it as their primary
        // tenant). Master DB write.
        using var masterConn = _masterFactory.CreateConnection();
        await masterConn.ExecuteAsync(new CommandDefinition(@"
IF NOT EXISTS (
    SELECT 1 FROM [master].[UserTenantMap]
    WHERE UserEmail = @email AND TenantId = @tenantId)
INSERT INTO [master].[UserTenantMap]
    (Id, UserEmail, TenantId, IsDefault, CreatedAt)
VALUES (NEWID(), @email, @tenantId, 1, SYSUTCDATETIME());",
            new { email = adminEmail, tenantId },
            cancellationToken: ct));
    }

    private async Task UpdateTenantStatusAsync(
        Guid tenantId, string fromStatus, string toStatus,
        Guid actorSuperAdminId, CancellationToken ct)
    {
        using var conn = _masterFactory.CreateConnection();
        var rowsAffected = await conn.ExecuteAsync(new CommandDefinition(@"
UPDATE [master].[Tenants]
SET Status = @toStatus,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @actorSuperAdminId
WHERE Id = @tenantId AND Status = @fromStatus;",
            new { tenantId, fromStatus, toStatus, actorSuperAdminId },
            cancellationToken: ct));

        if (rowsAffected == 0)
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' could not transition {fromStatus} → {toStatus} " +
                "(either not found or already at target state).");
    }

    private async Task SafeRollbackAsync(
        Guid tenantId, string dbName, string completedThrough, CancellationToken ct)
    {
        // Roll back in reverse order. Each cleanup wrapped in try so
        // partial-failure of one rollback step doesn't mask the original
        // exception.
        try
        {
            using var conn = _masterFactory.CreateConnection();
            // Drop DB if it was created (steps 2+).
            await conn.ExecuteAsync(new CommandDefinition(
                $"IF DB_ID(@dbName) IS NOT NULL " +
                $"BEGIN " +
                $"  ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"  DROP DATABASE [{dbName}]; " +
                $"END",
                new { dbName },
                commandTimeout: 60,
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback step 'drop DB {DbName}' failed.", dbName);
        }

        try
        {
            using var conn = _masterFactory.CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM [master].[Tenants] WHERE Id = @tenantId",
                new { tenantId },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback step 'delete master.Tenants row {TenantId}' failed.", tenantId);
        }
    }

    private string BuildTenantConnection(string dbName)
    {
        var template = _config.GetConnectionString(TenantTemplateKey)
            ?? throw new InvalidOperationException("ConnectionStrings:TenantTemplate not configured.");

        if (template.Contains("{0}", StringComparison.Ordinal))
            return string.Format(template, dbName);
        if (template.Contains("WMS_TenantTemplate", StringComparison.OrdinalIgnoreCase))
            return template.Replace("WMS_TenantTemplate", dbName, StringComparison.OrdinalIgnoreCase);

        var b = new SqlConnectionStringBuilder(template) { InitialCatalog = dbName };
        return b.ConnectionString;
    }
}
