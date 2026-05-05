using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504034L)]
[Tags("Tenant")]
public class Migration_20260504_034_CreateUsersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Users").InSchema("security")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Tenant-wide unique email — Users live inside the tenant DB so
            // the same email can recur across tenants (master.UserTenantMap
            // owns cross-tenant identity).
            .WithColumn("Email").AsAnsiString(100).NotNullable().Unique()

            // BCrypt hash payload — 60-char modular-crypt format with headroom
            // for future algorithms (Argon2id at 200+ chars). NVARCHAR isn't
            // needed because the hash output is ASCII.
            .WithColumn("PasswordHash").AsAnsiString(500).NotNullable()
            .WithColumn("FullName").AsString(200).Nullable()

            // Default true — newly-created users are usable immediately.
            // Admin disables for offboarding without deleting (preserves
            // audit history references).
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("LastLoginAt").AsDateTime2().Nullable()

            // Lockout policy: BLL increments FailedLoginAttempts on each
            // bad password; once a threshold is hit it stamps LockedUntil.
            // Reset to 0 on successful login. NULL LockedUntil = not locked.
            .WithColumn("FailedLoginAttempts").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LockedUntil").AsDateTime2().Nullable()

            // Per-user approval ceiling for adjustment / refund / write-off
            // workflows. NULL = no special limit (falls back to role default).
            .WithColumn("ApprovalLimit").AsDecimal(18, 2).Nullable()

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Active-users picker for admin / user-management screens.
        Create.Index("IX_Users_Active").OnTable("Users").InSchema("security")
            .OnColumn("IsActive").Ascending()
            .OnColumn("Email").Ascending();
    }

    public override void Down() =>
        Delete.Table("Users").InSchema("security");
}
