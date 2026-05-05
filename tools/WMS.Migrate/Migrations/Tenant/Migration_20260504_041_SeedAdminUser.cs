using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Seeds the bootstrap admin user and grants the ADMIN role assignment
// that lets a tenant be administered immediately after migration. The
// password hash below is BCrypt of the temporary password
// "ChangeMe!2026" with workFactor 12.
//
// ⚠️ The hash is committed to git history — anyone with repo access
// can derive that the default credential is "ChangeMe!2026". The
// operator MUST change it immediately on first login.
//
// Idempotent: the user is existence-checked by Email and the role
// assignment is existence-checked by the (Email, Code) pair, so re-runs
// after a partial failure are safe.
[Migration(20260504041L)]
[Tags("Tenant")]
public class Migration_20260504_041_SeedAdminUser : MigrationBase
{
    private const string AdminEmail = "nwuthipongworachoke@gmail.com";
    private const string AdminFullName = "System Administrator";

    // BCrypt hash of "ChangeMe!2026" with workFactor 12. Round-tripped
    // against BCrypt.Net-Next 4.1.0 at the time this migration was
    // authored.
    private const string AdminPasswordHash =
        "$2a$12$YnBfSkl02dL9wG8tBUXcP.G5Od76FEIBU7jwHgwjcEZr0i9NGMVSa";

    public override void Up()
    {
        // 1) Insert the admin user. Idempotent by Email (unique).
        Execute.Sql(
            $"IF NOT EXISTS (SELECT 1 FROM [security].[Users] WHERE Email = '{AdminEmail}') " +
            $"INSERT INTO [security].[Users] " +
            $"(Id, Email, PasswordHash, FullName, IsActive, FailedLoginAttempts, CreatedAt) " +
            $"VALUES (NEWID(), '{AdminEmail}', '{AdminPasswordHash}', " +
            $"N'{AdminFullName}', 1, 0, SYSUTCDATETIME());");

        // 2) Grant the ADMIN role. The lookup-and-insert pattern resolves
        // both UUIDs from their natural keys, so this migration doesn't
        // need to know the IDs assigned by 040 / above.
        Execute.Sql(@"
IF NOT EXISTS (
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
        // their actor pointer. Empty in this rebuild but defensive for
        // populated DBs.
        Execute.Sql(
            $"UPDATE [security].[AuditLog] SET UserId = NULL " +
            $"WHERE UserId = (SELECT Id FROM [security].[Users] WHERE Email = '{AdminEmail}');");

        // Deleting the user cascades the UserRoles row via FK_UserRoles_Users.
        Execute.Sql(
            $"DELETE FROM [security].[Users] WHERE Email = '{AdminEmail}';");
    }
}
