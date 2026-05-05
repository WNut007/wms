using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504035L)]
[Tags("Tenant")]
public class Migration_20260504_035_CreateRolesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Roles").InSchema("security")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Tenant-wide unique role code (e.g. 'ADMIN', 'PICKER', 'PACKER',
            // 'MANAGER'). Codes drive permission lookup; Names are the
            // operator-facing label.
            .WithColumn("Code").AsAnsiString(50).NotNullable().Unique()
            .WithColumn("Name").AsString(100).NotNullable()
            .WithColumn("Description").AsString(500).Nullable()

            // Built-in roles (Admin/Picker/Packer/Manager) get IsSystemRole
            // = 1 and the BLL refuses delete on those rows. Tenant-defined
            // custom roles default to 0 and are freely deletable.
            .WithColumn("IsSystemRole").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Active-roles picker — composite covers the typical "show all
        // active roles ordered by code" pattern.
        Create.Index("IX_Roles_Active").OnTable("Roles").InSchema("security")
            .OnColumn("IsActive").Ascending()
            .OnColumn("Code").Ascending();
    }

    public override void Down() =>
        Delete.Table("Roles").InSchema("security");
}
