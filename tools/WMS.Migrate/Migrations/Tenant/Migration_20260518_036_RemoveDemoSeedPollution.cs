using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 30A.2 (P0-6 fix): retroactively remove demo-seed pollution from
// tenant DBs provisioned BEFORE migrations 052/060/508_005/006/007 were
// gated.
//
// Background: those 5 migrations were tagged [Tags("Tenant")] and ran
// against every tenant DB the Phase 26+ fan-out coordinator saw —
// including new tenants created via SuperAdmin onboarding. Each newly
// onboarded customer received 24 demo warehouses (WH-DM*), 25 demo
// customers (CUST-XXXX), 24 demo products (PROD-XXXX), 1 demo category,
// 1 demo product (DEMO-001), 2 demo zones (RECV + STORAGE), and 2 demo
// locations (RECV-01 + STORAGE-A1). The bug looked like cross-tenant
// data leakage to onboarded admins.
//
// What this migration does NOT remove:
//   - UoM 'EA' — functional default every tenant uses for base count
//   - Owner 'SELF' — functional default for tenant-owned stock
//   - WH-MAIN — the bootstrap warehouse from migration 046
//
// Gate: runs against every tenant DB EXCEPT 'WMS_Tenant_Template' (the
// DEMO tenant's DB — demo data is intentional there).
//
// Safety: each DELETE includes NOT EXISTS guards against operational
// data (Stock, POs, SOs, allocations, pick/pack/ship tasks). If any
// polluting row has been adopted as real operational data, it's left
// in place + the cleanup continues with the remaining rows. Subsequent
// runs (after manual unwind) pick up where the previous run left off.
//
// Order: reverse-dependency (children before parents) so FK constraints
// don't block.
[Migration(20260518036L)]
[Tags("Tenant")]
public class Migration_20260518_036_RemoveDemoSeedPollution : MigrationBase
{
    private const string TemplateDbName = "WMS_Tenant_Template";

    public override void Up()
    {
        // 1) PROD-XXXX products (Migration_508_005).
        //    Refs: inventory.Stock, inbound.PurchaseOrderLines,
        //          outbound.SalesOrderLines.
        //    (Pick/Pack/Cycle/Adjustment ref via Stock → covered transitively.)
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE p
    FROM [master].[Products] p
    WHERE p.Code LIKE 'PROD-[0-9][0-9][0-9][0-9]'
      AND NOT EXISTS (SELECT 1 FROM [inventory].[Stock] s WHERE s.ProductId = p.Id)
      AND NOT EXISTS (SELECT 1 FROM [inbound].[PurchaseOrderLines] pol WHERE pol.ProductId = p.Id)
      AND NOT EXISTS (SELECT 1 FROM [outbound].[SalesOrderLines] sol WHERE sol.ProductId = p.Id);
END");

        // 2) DEMO-001 product (Migration_052).
        //    Same operational refs as PROD-XXXX.
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE p
    FROM [master].[Products] p
    WHERE p.Code = 'DEMO-001'
      AND NOT EXISTS (SELECT 1 FROM [inventory].[Stock] s WHERE s.ProductId = p.Id)
      AND NOT EXISTS (SELECT 1 FROM [inbound].[PurchaseOrderLines] pol WHERE pol.ProductId = p.Id)
      AND NOT EXISTS (SELECT 1 FROM [outbound].[SalesOrderLines] sol WHERE sol.ProductId = p.Id);
END");

        // 3) ProductCategory 'DEMO' (Migration_052).
        //    Refs: master.Products (CategoryId).
        //    If DEMO-001 + PROD-XXXX cleaned up above, this is safe.
        //    If any product still holds CategoryId=DEMO, skip + log.
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE c
    FROM [master].[ProductCategories] c
    WHERE c.Code = 'DEMO'
      AND NOT EXISTS (SELECT 1 FROM [master].[Products] p WHERE p.CategoryId = c.Id);
END");

        // 4) CUST-XXXX customers (Migration_508_006).
        //    Refs: outbound.SalesOrders (CustomerId).
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE c
    FROM [master].[Customers] c
    WHERE c.Code LIKE 'CUST-[0-9][0-9][0-9][0-9]'
      AND NOT EXISTS (SELECT 1 FROM [outbound].[SalesOrders] so WHERE so.CustomerId = c.Id);
END");

        // 5) RECV-01 location (Migration_052).
        //    Refs: inventory.Stock (LocationId), inventory.TransferOrderLines
        //          (FromLocationId / ToLocationId), master.PackStations.
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE l
    FROM [master].[Locations] l
    INNER JOIN [master].[Warehouses] w ON w.Id = l.WarehouseId
    WHERE w.Code = 'WH-MAIN' AND l.Code = 'RECV-01'
      AND NOT EXISTS (SELECT 1 FROM [inventory].[Stock] s WHERE s.LocationId = l.Id)
      AND NOT EXISTS (SELECT 1 FROM [inventory].[TransferOrderLines] tol
                      WHERE tol.FromLocationId = l.Id OR tol.ToLocationId = l.Id);
END");

        // 6) RECV zone (Migration_052).
        //    Refs: master.Locations (ZoneId).
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE z
    FROM [master].[Zones] z
    INNER JOIN [master].[Warehouses] w ON w.Id = z.WarehouseId
    WHERE w.Code = 'WH-MAIN' AND z.Code = 'RECV'
      AND NOT EXISTS (SELECT 1 FROM [master].[Locations] l WHERE l.ZoneId = z.Id);
END");

        // 7) STORAGE-A1 location (Migration_060).
        //    Same refs as RECV-01.
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE l
    FROM [master].[Locations] l
    INNER JOIN [master].[Warehouses] w ON w.Id = l.WarehouseId
    WHERE w.Code = 'WH-MAIN' AND l.Code = 'STORAGE-A1'
      AND NOT EXISTS (SELECT 1 FROM [inventory].[Stock] s WHERE s.LocationId = l.Id)
      AND NOT EXISTS (SELECT 1 FROM [inventory].[TransferOrderLines] tol
                      WHERE tol.FromLocationId = l.Id OR tol.ToLocationId = l.Id);
END");

        // 8) STORAGE zone (Migration_060).
        //    Refs: master.Locations (ZoneId).
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE z
    FROM [master].[Zones] z
    INNER JOIN [master].[Warehouses] w ON w.Id = z.WarehouseId
    WHERE w.Code = 'WH-MAIN' AND z.Code = 'STORAGE'
      AND NOT EXISTS (SELECT 1 FROM [master].[Locations] l WHERE l.ZoneId = z.Id);
END");

        // 9) WH-DM* warehouses (Migration_508_007).
        //    Refs: master.Zones (WarehouseId), master.Locations (WarehouseId).
        //    Demo warehouses had no child zones/locations at seed time —
        //    the guard is for tenants that may have built on them.
        Execute.Sql($@"
IF DB_NAME() <> '{TemplateDbName}'
BEGIN
    DELETE w
    FROM [master].[Warehouses] w
    WHERE w.Code LIKE 'WH-DM%'
      AND NOT EXISTS (SELECT 1 FROM [master].[Zones] z WHERE z.WarehouseId = w.Id)
      AND NOT EXISTS (SELECT 1 FROM [master].[Locations] l WHERE l.WarehouseId = w.Id);
END");
    }

    public override void Down()
    {
        // Restoration would require re-running the demo-seed migrations
        // 052/060/508_005/006/007. Their gates would skip non-template
        // DBs anyway, so a literal Down() in a non-template DB is a no-op.
        //
        // If demo data is needed in a non-template DB for some reason,
        // operator must either:
        //   (a) Use the master-data admin UI (Phase 7+) to create entries
        //       manually, or
        //   (b) Temporarily rename the DB to 'WMS_Tenant_Template' and
        //       re-run the seed migrations against it.
        //
        // No-op Down() — explicit comment is the contract.
    }
}
