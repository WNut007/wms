using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 6B (T6): seed 24 demo products under codes PROD-0001..PROD-0024.
//
// Coexists with the 'DEMO-001' product seeded by migration 052 — codes
// are disjoint, no collision possible. Same FK-resolution-by-code idiom
// as 052: looks up master.ProductCategories.Code='DEMO' and
// master.UnitsOfMeasure.Code='EA' rather than hardcoding GUIDs.
//
// Phase 30A.2 (P0-6 fix): gated on DB_NAME() = 'WMS_Tenant_Template' so
// the demo catalogue only lands in the template DB. Before the gate,
// every new tenant DB provisioned via SuperAdmin onboarding picked up
// 24 dummy products that looked like cross-tenant pollution to the
// onboarded customer.
//
// Idempotent — single INSERT...SELECT with NOT EXISTS guards every row,
// so re-running the migration after a partial failure is safe. Down()
// uses a symmetric DELETE...JOIN against the same VALUES list so partial
// rollbacks also clean up correctly.
//
// Status mix: 19 Active + 3 Discontinued + 2 Draft (~80/12/8 — close to
// the brief's ~80/10/10 target). All values PascalCase per the schema's
// CK_Products_Status check; the controller boundary handles the mock
// UI's lowercase wire format (Q4).
//
// Brand populated for every row — column added by migration 003.
// TrackingMethod stays 'None' (default; lot tracking opt-in per SKU).
// VelocityClass stays NULL (no ABC analysis run yet).
[Migration(20260508005L)]
[Tags("Tenant")]
public class Migration_20260508_005_SeedDemoProducts : MigrationBase
{
    private const string TemplateDbName = "WMS_Tenant_Template";

    private const string SeedValues = @"
    (VALUES
        ('PROD-0001', N'Apple iPhone 15 Pro 256GB',         'Active',       N'Apple'),
        ('PROD-0002', N'Samsung Galaxy S24 Ultra',          'Active',       N'Samsung'),
        ('PROD-0003', N'Sony WH-1000XM5 Headphones',        'Active',       N'Sony'),
        ('PROD-0004', N'LG OLED C3 55-inch TV',             'Active',       N'LG'),
        ('PROD-0005', N'Apple MacBook Air M3 13-inch',      'Active',       N'Apple'),
        ('PROD-0006', N'Nike Air Force 1 White',            'Active',       N'Nike'),
        ('PROD-0007', N'Adidas Stan Smith',                 'Active',       N'Adidas'),
        ('PROD-0008', N'Uniqlo Heattech Crew Neck',         'Active',       N'Uniqlo'),
        ('PROD-0009', N'Zara Wool Blend Coat',              'Active',       N'Zara'),
        ('PROD-0010', N'Nestle Nescafe Gold 200g',          'Active',       N'Nestle'),
        ('PROD-0011', N'Unilever Dove Soap Bar 100g',       'Active',       N'Unilever'),
        ('PROD-0012', N'L''Oreal Paris Mascara',            'Active',       N'L''Oreal'),
        ('PROD-0013', N'IKEA Billy Bookcase',               'Active',       N'IKEA'),
        ('PROD-0014', N'Lego Star Wars Millennium Falcon',  'Active',       N'Lego'),
        ('PROD-0015', N'Hasbro Monopoly Classic',           'Active',       N'Hasbro'),
        ('PROD-0016', N'Apple AirPods Pro 2',               'Active',       N'Apple'),
        ('PROD-0017', N'Samsung Galaxy Buds Pro',           'Active',       N'Samsung'),
        ('PROD-0018', N'Sony PlayStation 5 Slim',           'Active',       N'Sony'),
        ('PROD-0019', N'Generic USB-C Cable 2m',            'Active',       N'Generic'),
        ('PROD-0020', N'Apple iPhone 13 128GB',             'Discontinued', N'Apple'),
        ('PROD-0021', N'Samsung Galaxy S22 256GB',          'Discontinued', N'Samsung'),
        ('PROD-0022', N'Sony WH-1000XM4 Headphones',        'Discontinued', N'Sony'),
        ('PROD-0023', N'Nike Air Max 2026 Preview',         'Draft',        N'Nike'),
        ('PROD-0024', N'Apple Vision Pro 2 Preview',        'Draft',        N'Apple')
    ) AS s (Code, Name, Status, Brand)";

    public override void Up() =>
        Execute.Sql($@"
IF DB_NAME() = '{TemplateDbName}'
BEGIN
    ;WITH seed AS (
        SELECT s.Code, s.Name, s.Status, s.Brand
        FROM {SeedValues}
    )
    INSERT INTO [master].[Products]
        (Id, Code, Name, CategoryId, BaseUomId,
         TrackingMethod, UseCatchWeight, Status, Brand, CreatedAt)
    SELECT NEWID(), s.Code, s.Name,
           cat.Id, uom.Id,
           'None', 0, s.Status, s.Brand, SYSUTCDATETIME()
    FROM seed s
    CROSS JOIN [master].[ProductCategories] cat
    CROSS JOIN [master].[UnitsOfMeasure] uom
    WHERE cat.Code = 'DEMO'
      AND uom.Code = 'EA'
      AND NOT EXISTS (
          SELECT 1 FROM [master].[Products] p WHERE p.Code = s.Code
      );
END");

    public override void Down() =>
        Execute.Sql($@"
IF DB_NAME() = '{TemplateDbName}'
BEGIN
    ;WITH seed AS (
        SELECT s.Code FROM {SeedValues}
    )
    DELETE p
    FROM [master].[Products] p
    INNER JOIN seed s ON p.Code = s.Code;
END");
}
