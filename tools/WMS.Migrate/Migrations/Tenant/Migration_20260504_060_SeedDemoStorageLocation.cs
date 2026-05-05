using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Adds a STORAGE zone + STORAGE-A1 location under WH-MAIN so the
// putaway flow has somewhere to move stock to. Mirrors the seed
// pattern in 052 (idempotent existence checks, dependency-ordered
// inserts).
[Migration(20260504060L)]
[Tags("Tenant")]
public class Migration_20260504_060_SeedDemoStorageLocation : MigrationBase
{
    public override void Up()
    {
        // 1) STORAGE zone under WH-MAIN. Type 'Storage' is the bin-area
        // bucket (Receiving / Storage / Picking / Packing / ...).
        Execute.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM [master].[Zones] z
    JOIN [master].[Warehouses] w ON w.Id = z.WarehouseId
    WHERE w.Code = 'WH-MAIN' AND z.Code = 'STORAGE'
)
INSERT INTO [master].[Zones]
    (Id, WarehouseId, Code, Name, Type, LotCommingleAllowed, IsActive, CreatedAt)
SELECT NEWID(), w.Id, 'STORAGE', N'Storage Zone', 'Storage', 1, 1, SYSUTCDATETIME()
FROM [master].[Warehouses] w
WHERE w.Code = 'WH-MAIN';");

        // 2) STORAGE-A1 location inside STORAGE. Aisle / Bay / Level
        // populated for future 3D viz (ADR-011 keeps the schema warm
        // even though Phase 1 doesn't render).
        Execute.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM [master].[Locations] l
    JOIN [master].[Warehouses] w ON w.Id = l.WarehouseId
    WHERE w.Code = 'WH-MAIN' AND l.Code = 'STORAGE-A1'
)
INSERT INTO [master].[Locations]
    (Id, WarehouseId, ZoneId, Code, CapacityPolicy, BinRank,
     AllowMultipleItems, Rotation, Show3D, IsPickface,
     Status, IsActive, CreatedAt,
     Aisle, Bay, Level)
SELECT NEWID(), w.Id, z.Id, 'STORAGE-A1', 'NoLimit', 100,
       1, 0, 1, 0,
       'Active', 1, SYSUTCDATETIME(),
       'A', 1, 1
FROM [master].[Warehouses] w
JOIN [master].[Zones] z ON z.WarehouseId = w.Id AND z.Code = 'STORAGE'
WHERE w.Code = 'WH-MAIN';");
    }

    public override void Down()
    {
        // Reverse dependency order — location first, then zone.
        Execute.Sql(@"
DELETE l
FROM [master].[Locations] l
JOIN [master].[Warehouses] w ON w.Id = l.WarehouseId
WHERE w.Code = 'WH-MAIN' AND l.Code = 'STORAGE-A1';");

        Execute.Sql(@"
DELETE z
FROM [master].[Zones] z
JOIN [master].[Warehouses] w ON w.Id = z.WarehouseId
WHERE w.Code = 'WH-MAIN' AND z.Code = 'STORAGE';");
    }
}
