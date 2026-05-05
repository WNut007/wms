using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504036L)]
[Tags("Tenant")]
public class Migration_20260504_036_CreateFunctionsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Functions").InSchema("security")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Tenant-wide unique function code in dotted form
            // (e.g. 'INVENTORY.STOCK', 'OUTBOUND.ORDERS'). The dotted prefix
            // mirrors Module so permission lookup can reason about either.
            .WithColumn("Code").AsAnsiString(50).NotNullable().Unique()
            .WithColumn("Name").AsString(100).NotNullable()

            // Module groups functions for menu rendering and bulk
            // permission grants ("grant all Inbound functions to Receiver").
            .WithColumn("Module").AsAnsiString(50).NotNullable()
            .WithColumn("Description").AsString(500).Nullable()
            // Lower number = higher priority in module menu rendering.
            .WithColumn("DisplayOrder").AsInt32().Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Module-grouped active-functions lookup ("show every active
        // Outbound function ordered for the menu").
        Create.Index("IX_Functions_Module").OnTable("Functions").InSchema("security")
            .OnColumn("Module").Ascending()
            .OnColumn("IsActive").Ascending()
            .OnColumn("DisplayOrder").Ascending();
    }

    public override void Down() =>
        Delete.Table("Functions").InSchema("security");
}
