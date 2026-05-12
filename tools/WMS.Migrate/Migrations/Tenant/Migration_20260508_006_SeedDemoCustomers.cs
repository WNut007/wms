using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 6B (T6): seed 25 demo customers under codes CUST-0001..CUST-0025.
//
// 17 B2C + 8 B2B (mock-flavoured 70/30 mix). Status mix: 21 Active +
// 2 Suspended + 2 Draft (within the brief's ~80/10/10 target). All
// values PascalCase per CK_Customers_Type and CK_Customers_Status; the
// controller boundary handles the mock UI's lowercase 'pending' /
// 'inactive' wire values (Q4 — 'pending' maps to 'Draft').
//
// Phase 30A.2 (P0-6 fix): gated on DB_NAME() = 'WMS_Tenant_Template' so
// the demo customer list only lands in the template DB. Before the
// gate, every new tenant DB picked up 25 dummy customers — onboarded
// admins saw a list that looked like another tenant's data.
//
// Country populated for every row — column added by migration 004.
// CompanyName + TaxId populated for B2B only (BLL convention; not DB-
// enforced — see migration 018 comment for why).
//
// PreferredCarrierId left NULL — seed customers don't pre-bind to
// specific carriers. The FK to master.Carriers (migration 027) is
// ON DELETE SET NULL so that's safe long-term too.
//
// Idempotent: NOT EXISTS guard per row, symmetric Down via DELETE-JOIN.
[Migration(20260508006L)]
[Tags("Tenant")]
public class Migration_20260508_006_SeedDemoCustomers : MigrationBase
{
    private const string TemplateDbName = "WMS_Tenant_Template";

    private const string SeedValues = @"
    (VALUES
        -- B2C (17)
        ('CUST-0001', N'Ananya Srisuk',          'B2C', NULL, NULL, N'ananya.s@example.com',           'Active',    N'Thailand'),
        ('CUST-0002', N'Somchai Phongphan',      'B2C', NULL, NULL, N'somchai.p@example.com',          'Active',    N'Thailand'),
        ('CUST-0003', N'Pim Boonrueng',          'B2C', NULL, NULL, N'pim.b@example.com',              'Active',    N'Thailand'),
        ('CUST-0004', N'Nattakorn Chai',         'B2C', NULL, NULL, N'nattakorn.c@example.com',        'Active',    N'Thailand'),
        ('CUST-0005', N'Wichai Kaewkamol',       'B2C', NULL, NULL, N'wichai.k@example.com',           'Active',    N'Thailand'),
        ('CUST-0006', N'Suda Ngamtrakulchol',    'B2C', NULL, NULL, N'suda.n@example.com',             'Active',    N'Thailand'),
        ('CUST-0007', N'Apinya Saengthong',      'B2C', NULL, NULL, N'apinya.s@example.com',           'Active',    N'Thailand'),
        ('CUST-0008', N'Thanat Jaidee',          'B2C', NULL, NULL, N'thanat.j@example.com',           'Suspended', N'Thailand'),
        ('CUST-0009', N'Mali Saengthong',        'B2C', NULL, NULL, N'mali.s@example.com',             'Active',    N'Thailand'),
        ('CUST-0010', N'Lim Wei Ming',           'B2C', NULL, NULL, N'lim.wm@example.sg',              'Active',    N'Singapore'),
        ('CUST-0011', N'Nguyen Van Anh',         'B2C', NULL, NULL, N'anh.nv@example.vn',              'Active',    N'Vietnam'),
        ('CUST-0012', N'Siti Nurhaliza',         'B2C', NULL, NULL, N'siti.n@example.my',              'Active',    N'Malaysia'),
        ('CUST-0013', N'Budi Santoso',           'B2C', NULL, NULL, N'budi.s@example.id',              'Active',    N'Indonesia'),
        ('CUST-0014', N'Maria Santos',           'B2C', NULL, NULL, N'maria.s@example.ph',             'Active',    N'Philippines'),
        ('CUST-0015', N'Tan Mei Lin',            'B2C', NULL, NULL, N'tan.ml@example.sg',              'Draft',     N'Singapore'),
        ('CUST-0016', N'Pim Sriwattana',         'B2C', NULL, NULL, N'pim.sw@example.com',             'Active',    N'Thailand'),
        ('CUST-0017', N'Nat Boonpaisan',         'B2C', NULL, NULL, N'nat.b@example.com',              'Active',    N'Thailand'),
        -- B2B (8)
        ('CUST-0018', N'Bangkok Trading Co Ltd', 'B2B', N'Bangkok Trading Co., Ltd.',  N'0107537001234', N'ops@bangkoktrading.co.th',     'Active',    N'Thailand'),
        ('CUST-0019', N'TechCorp Solutions',     'B2B', N'TechCorp Solutions Pte Ltd', N'201234567K',    N'purchasing@techcorp.sg',       'Active',    N'Singapore'),
        ('CUST-0020', N'Pacific Imports VN',     'B2B', N'Pacific Imports Vietnam Co', N'0312345678',    N'ceo@pacific-imports.vn',       'Active',    N'Vietnam'),
        ('CUST-0021', N'Sky Foods Indonesia',    'B2B', N'PT Sky Foods Indonesia',     N'01.234.567.8', N'sales@skyfoods.id',             'Active',    N'Indonesia'),
        ('CUST-0022', N'Northstar Logistics',    'B2B', N'Northstar Logistics Sdn Bhd', N'201912345678', N'admin@northstar.my',           'Active',    N'Malaysia'),
        ('CUST-0023', N'Bluebird Supply',        'B2B', N'Bluebird Supply Inc',        N'009-876-543',   N'orders@bluebird.ph',           'Suspended', N'Philippines'),
        ('CUST-0024', N'Indigo Group Holdings',  'B2B', N'Indigo Group Holdings Co',   N'0107538009876', N'contact@indigogroup.co.th',    'Active',    N'Thailand'),
        ('CUST-0025', N'Acme Trading',           'B2B', N'Acme Trading Co Ltd',        N'0107536007654', N'hello@acme-trading.co.th',     'Draft',     N'Thailand')
    ) AS s (Code, Name, CustomerType, CompanyName, TaxId, Email, Status, Country)";

    public override void Up() =>
        Execute.Sql($@"
IF DB_NAME() = '{TemplateDbName}'
BEGIN
    ;WITH seed AS (
        SELECT s.Code, s.Name, s.CustomerType, s.CompanyName, s.TaxId,
               s.Email, s.Status, s.Country
        FROM {SeedValues}
    )
    INSERT INTO [master].[Customers]
        (Id, Code, Name, CustomerType, CompanyName, TaxId,
         Email, Status, Country, CreatedAt)
    SELECT NEWID(), s.Code, s.Name, s.CustomerType, s.CompanyName, s.TaxId,
           s.Email, s.Status, s.Country, SYSUTCDATETIME()
    FROM seed s
    WHERE NOT EXISTS (
        SELECT 1 FROM [master].[Customers] c WHERE c.Code = s.Code
    );
END");

    public override void Down() =>
        Execute.Sql($@"
IF DB_NAME() = '{TemplateDbName}'
BEGIN
    ;WITH seed AS (
        SELECT s.Code FROM {SeedValues}
    )
    DELETE c
    FROM [master].[Customers] c
    INNER JOIN seed s ON c.Code = s.Code;
END");
}
