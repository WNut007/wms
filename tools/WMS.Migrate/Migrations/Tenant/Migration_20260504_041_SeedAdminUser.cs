using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Seeds the bootstrap admin user for the DEMO tenant's template DB.
// Password hash below is BCrypt of "ChangeMe!2026" with workFactor 12.
//
// ⚠️ The hash is committed to git history — anyone with repo access
// can derive that the default credential is "ChangeMe!2026". The
// operator MUST change it immediately on first login.
//
// P0 #1 / #5 fix (post-30A): GATED on DB_NAME() = 'WMS_Tenant_Template'.
// Before the gate, this migration ran against every tenant DB the
// Phase 26 fan-out coordinator saw (including new ones created via
// SuperAdmin onboarding), which leaked the dev-time admin email +
// password into every customer's tenant DB and broke
// TenantProvisioningService.ResetTenantAdminPasswordAsync's
// "first ADMIN" lookup. The gate confines the seed to the DEMO
// template only; new tenants now have a clean security.Users.
//
// Idempotent: existence-checked by Email and the (Email, Code) pair.
// FluentMigrator's VersionInfo per-DB tracks application; the inner
// IF NOT EXISTS guards a re-application against the template if
// VersionInfo is missing (e.g. fresh schema).
[Migration(20260504041L)]
[Tags("Tenant")]
public class Migration_20260504_041_SeedAdminUser : MigrationBase
{
    private const string AdminEmail = "nwuthipongworachoke@gmail.com";
    private const string AdminFullName = "System Administrator";
    private const string TemplateDbName = "WMS_Tenant_Template";

    // BCrypt hash of "ChangeMe!2026" with workFactor 12. Round-tripped
    // against BCrypt.Net-Next 4.1.0 at the time this migration was
    // authored.
    private const string AdminPasswordHash =
        "$2a$12$YnBfSkl02dL9wG8tBUXcP.G5Od76FEIBU7jwHgwjcEZr0i9NGMVSa";

    public override void Up()
    {
        // 1) Insert the admin user. Gated on DB_NAME() so this seed
        // only lands in the DEMO template; new tenant DBs created via
        // SuperAdmin onboarding skip it.
        Execute.Sql(
            $"IF DB_NAME() = '{TemplateDbName}' " +
            $"AND NOT EXISTS (SELECT 1 FROM [security].[Users] WHERE Email = '{AdminEmail}') " +
            $"INSERT INTO [security].[Users] " +
            $"(Id, Email, PasswordHash, FullName, IsActive, FailedLoginAttempts, CreatedAt) " +
            $"VALUES (NEWID(), '{AdminEmail}', '{AdminPasswordHash}', " +
            $"N'{AdminFullName}', 1, 0, SYSUTCDATETIME());");

        // 2) Grant the ADMIN role. Same gate.
        Execute.Sql(@"
IF DB_NAME() = '" + TemplateDbName + @"'
AND NOT EXISTS (
    SELECT 1
    FROM [security].[UserRoles] ur
    JOIN [security].[Users] u ON u.Id = ur.UserId
    JOIN [security].[Roles] r ON r.Id = ur.RoleId
    WHERE u.Email = '" + AdminEmail + @"' AND r.Code = 'ADMIN'
)
INSERT INTO [security].[UserRoles] (Id, UserId, RoleId, CreatedAt)
SELECT NEWID(), u.Id, r.Id, SYSUTCDATETIME()
FROM [security].[Users] u
CROSS JOIN [security].[Roles] r
WHERE u.Email = '" + AdminEmail + @"' AND r.Code = 'ADMIN';");
    }

    public override void Down()
    {
        // Audit history references UserId with ON DELETE NO ACTION, so
        // the admin's audit rows would block the user delete. Null the
        // FK first — the events still exist in the log; they just lose
        // their actor pointer.
        Execute.Sql(
            $"IF DB_NAME() = '{TemplateDbName}' " +
            $"UPDATE [security].[AuditLog] SET UserId = NULL " +
            $"WHERE UserId = (SELECT Id FROM [security].[Users] WHERE Email = '{AdminEmail}');");

        // Deleting the user cascades the UserRoles row via FK_UserRoles_Users.
        Execute.Sql(
            $"IF DB_NAME() = '{TemplateDbName}' " +
            $"DELETE FROM [security].[Users] WHERE Email = '{AdminEmail}';");
    }
}
