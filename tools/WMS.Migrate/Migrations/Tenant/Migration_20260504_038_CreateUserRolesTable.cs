using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504038L)]
[Tags("Tenant")]
public class Migration_20260504_038_CreateUserRolesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("UserRoles").InSchema("security")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // CASCADE on both sides: a deleted User or Role drops the
            // assignment. Soft-delete via IsActive on the parents is the
            // normal disable path.
            .WithColumn("UserId").AsGuid().NotNullable()
                .ForeignKey("FK_UserRoles_Users",
                            "security", "Users", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("RoleId").AsGuid().NotNullable()
                .ForeignKey("FK_UserRoles_Roles",
                            "security", "Roles", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            // Time-bounded role validity. NULL ValidFrom = effective
            // immediately on insert; NULL ValidTo = no expiry. Permission
            // resolver uses (NOW BETWEEN ValidFrom AND ValidTo) as a
            // pre-filter so expired assignments stay in the row for audit.
            .WithColumn("ValidFrom").AsDateTime2().Nullable()
            .WithColumn("ValidTo").AsDateTime2().Nullable()

            // Who granted the role — soft FK to Users (intentionally
            // unconstrained to avoid circular CASCADE: deleting an admin
            // shouldn't tear down every assignment they made).
            .WithColumn("AssignedBy").AsGuid().Nullable()

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // One assignment row per (User, Role) — unique index doubles as
        // the constraint and the primary lookup path ("which roles does
        // user X hold?").
        Create.Index("UX_UserRoles_User_Role")
            .OnTable("UserRoles").InSchema("security")
            .WithOptions().Unique()
            .OnColumn("UserId").Ascending()
            .OnColumn("RoleId").Ascending();

        // Reverse lookup ("which users hold role Y?").
        Create.Index("IX_UserRoles_Role")
            .OnTable("UserRoles").InSchema("security")
            .OnColumn("RoleId").Ascending()
            .OnColumn("UserId").Ascending();
    }

    public override void Down() =>
        Delete.Table("UserRoles").InSchema("security");
}
