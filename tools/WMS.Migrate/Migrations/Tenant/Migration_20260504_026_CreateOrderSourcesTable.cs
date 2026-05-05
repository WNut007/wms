using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504026L)]
[Tags("Tenant")]
public class Migration_20260504_026_CreateOrderSourcesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("OrderSources").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Tenant-wide unique code (e.g. 'NORMAL', 'EDI', 'WEB', 'PORTAL',
            // 'PHONE', 'EMAIL', 'IMPORT', 'API'). No CHECK on Code — free-form
            // string lets tenants add their own origin types without a schema
            // change.
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(50).NotNullable()

            // Default false — most order sources flow straight through.
            // Set true for sources that need CSR review before allocation
            // (e.g. high-risk PHONE / EMAIL imports).
            .WithColumn("RequiresApproval").AsBoolean().NotNullable().WithDefaultValue(0)
            // Default false — opt-in flag. Marks the source as eligible for
            // the auto-sync poller (EDI / API push integrations).
            .WithColumn("AllowAutoSync").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("Description").AsString(500).Nullable()
            // Lower number = higher priority in source selection UI.
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

        // Active-sources lookup for order-entry dropdowns, ordered for UI.
        Create.Index("IX_OrderSources_Active")
            .OnTable("OrderSources").InSchema("master")
            .OnColumn("IsActive").Ascending()
            .OnColumn("DisplayOrder").Ascending();
    }

    public override void Down() =>
        Delete.Table("OrderSources").InSchema("master");
}
