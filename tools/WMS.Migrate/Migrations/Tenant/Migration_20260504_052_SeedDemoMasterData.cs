using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Seeds the minimum master-data graph needed for the receiving primitive
// to write a Stock row under WH-MAIN. Each insert is existence-checked
// by its natural key (Code or Code+parent) so re-runs after a partial
// failure are safe; Down() reverses the inserts in dependency order.
//
// The chain — UoM → ProductCategory → Owner → Zone → Location → Product
// — is the smallest set of FK targets inventory.Stock requires
// (Location, Product, Owner, UoM). Lot and Pallet stay out, matching
// the Receiving-α scope.
[Migration(20260504052L)]
[Tags("Tenant")]
public class Migration_20260504_052_SeedDemoMasterData : MigrationBase
{
    public override void Up()
    {
        // 1) UnitOfMeasure 'EA' — base count unit, used by every product
        // that doesn't track weight or volume natively.
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [master].[UnitsOfMeasure] WHERE Code = 'EA')
INSERT INTO [master].[UnitsOfMeasure]
    (Id, Code, Name, Type, IsBase, IsActive, CreatedAt)
VALUES
    (NEWID(), 'EA', N'Each', 'Count', 1, 1, SYSUTCDATETIME());");

        // 2) ProductCategory 'DEMO' — flat category, no parent.
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [master].[ProductCategories] WHERE Code = 'DEMO')
INSERT INTO [master].[ProductCategories]
    (Id, Code, Name, IsActive, CreatedAt)
VALUES
    (NEWID(), 'DEMO', N'Demo Category', 1, SYSUTCDATETIME());");

        // 3) Owner 'SELF' — the tenant's own stock (most common case).
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [master].[Owners] WHERE Code = 'SELF')
INSERT INTO [master].[Owners]
    (Id, Code, Name, OwnerType, HasPortalAccess, IsActive, CreatedAt)
VALUES
    (NEWID(), 'SELF', N'Self-owned Inventory', 'Self', 0, 1, SYSUTCDATETIME());");

        // 4) Zone 'RECV' under WH-MAIN — receiving zone where new stock
        // first lands. Composite uniqueness is (WarehouseId, Code) so the
        // existence check resolves the warehouse Id by Code.
        Execute.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM [master].[Zones] z
    JOIN [master].[Warehouses] w ON w.Id = z.WarehouseId
    WHERE w.Code = 'WH-MAIN' AND z.Code = 'RECV'
)
INSERT INTO [master].[Zones]
    (Id, WarehouseId, Code, Name, Type, LotCommingleAllowed, IsActive, CreatedAt)
SELECT NEWID(), w.Id, 'RECV', N'Receiving Zone', 'Receiving', 1, 1, SYSUTCDATETIME()
FROM [master].[Warehouses] w
WHERE w.Code = 'WH-MAIN';");

        // 5) Location 'RECV-01' inside RECV. Default capacity / rank
        // values match the Locations table defaults — receiving docks
        // don't need fine-grained capacity rules.
        Execute.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM [master].[Locations] l
    JOIN [master].[Warehouses] w ON w.Id = l.WarehouseId
    WHERE w.Code = 'WH-MAIN' AND l.Code = 'RECV-01'
)
INSERT INTO [master].[Locations]
    (Id, WarehouseId, ZoneId, Code, CapacityPolicy, BinRank,
     AllowMultipleItems, Rotation, Show3D, IsPickface,
     Status, IsActive, CreatedAt)
SELECT NEWID(), w.Id, z.Id, 'RECV-01', 'NoLimit', 100,
       1, 0, 1, 0,
       'Active', 1, SYSUTCDATETIME()
FROM [master].[Warehouses] w
JOIN [master].[Zones] z ON z.WarehouseId = w.Id AND z.Code = 'RECV'
WHERE w.Code = 'WH-MAIN';");

        // 6) Product 'DEMO-001' — no lot/serial tracking, EA as base UoM,
        // category DEMO. Status 'Active' so it's immediately receivable.
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [master].[Products] WHERE Code = 'DEMO-001')
INSERT INTO [master].[Products]
    (Id, Code, Name, CategoryId, BaseUomId,
     TrackingMethod, UseCatchWeight, Status, CreatedAt)
SELECT NEWID(), 'DEMO-001', N'Demo Product 001',
       cat.Id, uom.Id,
       'None', 0, 'Active', SYSUTCDATETIME()
FROM [master].[ProductCategories] cat,
     [master].[UnitsOfMeasure] uom
WHERE cat.Code = 'DEMO' AND uom.Code = 'EA';");
    }

    public override void Down()
    {
        // Reverse dependency order — each delete is a no-op if the row
        // is missing, so partial-applied seeds clean up safely.
        Execute.Sql("DELETE FROM [master].[Products] WHERE Code = 'DEMO-001';");

        Execute.Sql(@"
DELETE l
FROM [master].[Locations] l
JOIN [master].[Warehouses] w ON w.Id = l.WarehouseId
WHERE w.Code = 'WH-MAIN' AND l.Code = 'RECV-01';");

        Execute.Sql(@"
DELETE z
FROM [master].[Zones] z
JOIN [master].[Warehouses] w ON w.Id = z.WarehouseId
WHERE w.Code = 'WH-MAIN' AND z.Code = 'RECV';");

        Execute.Sql("DELETE FROM [master].[Owners] WHERE Code = 'SELF';");
        Execute.Sql("DELETE FROM [master].[ProductCategories] WHERE Code = 'DEMO';");
        Execute.Sql("DELETE FROM [master].[UnitsOfMeasure] WHERE Code = 'EA';");
    }
}
