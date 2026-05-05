using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504008L)]
[Tags("Tenant")]
public class Migration_20260504_008_CreatePackStationsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("PackStations").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_PackStations_Warehouses", "master", "Warehouses", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Code").AsAnsiString(20).NotNullable()
            .WithColumn("Type").AsAnsiString(20).NotNullable()
            // Equipment flags — all default false; admin enables per station.
            .WithColumn("HasScale").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("HasWebcam").AsBoolean().NotNullable().WithDefaultValue(0)
            // ADR-009: pack-video recording flag (per-station override of channel default).
            .WithColumn("VideoEnabled").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("HasPrinter").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsAnsiString(20).NotNullable().WithDefaultValue("Available")
            // IsActive separates decommissioning (gone) from Status (operational state).
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        // One station code per warehouse — same code can repeat across warehouses.
        Create.Index("UX_PackStations_Warehouse_Code")
            .OnTable("PackStations").InSchema("master")
            .WithOptions().Unique()
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("Code").Ascending();

        // Primary lookup path: active stations per warehouse, filtered by Type
        // (Standard vs Express vs Returns dispatching).
        Create.Index("IX_PackStations_Warehouse")
            .OnTable("PackStations").InSchema("master")
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("IsActive").Ascending()
            .OnColumn("Type").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[PackStations] " +
            "ADD CONSTRAINT CK_PackStations_Type " +
            "CHECK (Type IN ('Standard', 'Express', 'Returns'));");

        Execute.Sql(
            "ALTER TABLE [master].[PackStations] " +
            "ADD CONSTRAINT CK_PackStations_Status " +
            "CHECK (Status IN ('Available', 'Occupied', 'Maintenance'));");
    }

    public override void Down() =>
        Delete.Table("PackStations").InSchema("master");
}
