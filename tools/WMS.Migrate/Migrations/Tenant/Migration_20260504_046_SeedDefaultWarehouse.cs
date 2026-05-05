using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Seeds the bootstrap warehouse ('WH-MAIN') so login Step 3
// (AuthController.SelectWarehouse) has at least one row to skip-select
// onto on a fresh install. Real warehouse setup happens through the
// admin UI later — until then this single Main warehouse doubles as
// the default working warehouse.
//
// Idempotent: existence-checked by Code (unique).
[Migration(20260504046L)]
[Tags("Tenant")]
public class Migration_20260504_046_SeedDefaultWarehouse : MigrationBase
{
    private const string WarehouseCode = "WH-MAIN";
    private const string WarehouseName = "Main Warehouse";

    public override void Up()
    {
        Execute.Sql(
            $"IF NOT EXISTS (SELECT 1 FROM [master].[Warehouses] WHERE Code = '{WarehouseCode}') " +
            $"INSERT INTO [master].[Warehouses] " +
            $"(Id, Code, Name, Type, IsActive, CreatedAt) " +
            $"VALUES (NEWID(), '{WarehouseCode}', N'{WarehouseName}', " +
            $"'Main', 1, SYSUTCDATETIME());");
    }

    public override void Down() =>
        Execute.Sql(
            $"DELETE FROM [master].[Warehouses] WHERE Code = '{WarehouseCode}';");
}
