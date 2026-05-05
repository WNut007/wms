using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504037L)]
[Tags("Tenant")]
public class Migration_20260504_037_CreateRoleFunctionPermissionsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("RoleFunctionPermissions").InSchema("security")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // CASCADE on both sides: a deleted Role / Function takes its
            // permission rows with it. Soft-delete via IsActive on the
            // parent rows is the normal disable path.
            .WithColumn("RoleId").AsGuid().NotNullable()
                .ForeignKey("FK_RoleFunctionPermissions_Roles",
                            "security", "Roles", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("FunctionId").AsGuid().NotNullable()
                .ForeignKey("FK_RoleFunctionPermissions_Functions",
                            "security", "Functions", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            // Function-CRUD matrix per ADR-010: each row is the explicit
            // capability for one (Role, Function). All flags default to 0
            // — permission must be granted, never assumed.
            .WithColumn("CanView").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("CanAdd").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("CanEdit").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("CanDelete").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("CanApprove").AsBoolean().NotNullable().WithDefaultValue(0)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // One permission row per (Role, Function) — unique index doubles
        // as the constraint and the primary lookup path for permission
        // resolution ("does role X have access to function Y?").
        Create.Index("UX_RoleFunctionPermissions_Role_Function")
            .OnTable("RoleFunctionPermissions").InSchema("security")
            .WithOptions().Unique()
            .OnColumn("RoleId").Ascending()
            .OnColumn("FunctionId").Ascending();

        // Reverse lookup ("which roles can view function Z?").
        Create.Index("IX_RoleFunctionPermissions_Function")
            .OnTable("RoleFunctionPermissions").InSchema("security")
            .OnColumn("FunctionId").Ascending()
            .OnColumn("RoleId").Ascending();
    }

    public override void Down() =>
        Delete.Table("RoleFunctionPermissions").InSchema("security");
}
