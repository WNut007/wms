using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504003L)]
[Tags("Tenant")]
public class Migration_20260504_003_CreateWarehouseDocksTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("WarehouseDocks").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_WarehouseDocks_Warehouses", "master", "Warehouses", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Code").AsAnsiString(20).NotNullable()
            .WithColumn("Name").AsString(100).Nullable()
            .WithColumn("Type").AsAnsiString(20).NotNullable()
            .WithColumn("Status").AsAnsiString(20).NotNullable().WithDefaultValue("Available")
            .WithColumn("Capacity").AsInt32().Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"));

        // One dock code per warehouse — same code can repeat across warehouses.
        Create.Index("UX_WarehouseDocks_Warehouse_Code")
            .OnTable("WarehouseDocks").InSchema("master")
            .WithOptions().Unique()
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("Code").Ascending();

        // Primary lookup path: active docks per warehouse, filtered by Type
        // (Receiving vs Shipping pickers).
        Create.Index("IX_Docks_Warehouse")
            .OnTable("WarehouseDocks").InSchema("master")
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("IsActive").Ascending()
            .OnColumn("Type").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[WarehouseDocks] " +
            "ADD CONSTRAINT CK_WarehouseDocks_Type " +
            "CHECK (Type IN ('Receiving', 'Shipping', 'Both'));");

        Execute.Sql(
            "ALTER TABLE [master].[WarehouseDocks] " +
            "ADD CONSTRAINT CK_WarehouseDocks_Status " +
            "CHECK (Status IN ('Available', 'Occupied', 'Maintenance'));");
    }

    public override void Down() =>
        Delete.Table("WarehouseDocks").InSchema("master");
}
