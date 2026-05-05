using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504006L)]
[Tags("Tenant")]
public class Migration_20260504_006_CreateZonesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Zones").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_Zones_Warehouses", "master", "Warehouses", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Code").AsAnsiString(20).NotNullable()
            .WithColumn("Name").AsString(100).NotNullable()
            .WithColumn("Type").AsAnsiString(30).NotNullable()
            .WithColumn("Temperature").AsAnsiString(20).Nullable()
            // Free-form list (comma or JSON) — restrictions are advisory metadata,
            // not enforced at DB level. Validated by BLL when stocking decisions run.
            .WithColumn("AllowedProductTypes").AsString(500).Nullable()
            // System default ALLOW per master design: lot commingle is permitted
            // unless a specific zone or product overrides it.
            .WithColumn("LotCommingleAllowed").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        // One zone code per warehouse — same code can repeat across warehouses
        // (e.g., every warehouse has its own 'PICK-A').
        Create.Index("UX_Zones_Warehouse_Code")
            .OnTable("Zones").InSchema("master")
            .WithOptions().Unique()
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("Code").Ascending();

        // Active-zones-per-warehouse lookup (sidebar pickers, putaway target lists).
        Create.Index("IX_Zones_Warehouse")
            .OnTable("Zones").InSchema("master")
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("IsActive").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Zones] " +
            "ADD CONSTRAINT CK_Zones_Type " +
            "CHECK (Type IN ('Receiving', 'Storage', 'Picking', 'Packing', " +
            "'Shipping', 'Staging', 'Quarantine', 'Returns'));");

        // Nullable check — NULL is permitted (zones without a temperature classification).
        Execute.Sql(
            "ALTER TABLE [master].[Zones] " +
            "ADD CONSTRAINT CK_Zones_Temperature " +
            "CHECK (Temperature IS NULL OR Temperature IN ('Ambient', 'Chilled', 'Frozen'));");
    }

    public override void Down() =>
        Delete.Table("Zones").InSchema("master");
}
