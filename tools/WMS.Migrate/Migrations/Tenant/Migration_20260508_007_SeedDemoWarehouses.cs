using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 6B (T6): seed 24 demo warehouses under codes WH-DM01..WH-DM24
// (DM = "demo" — disjoint from any real WH-* code, including WH-MAIN
// already seeded by migration 046).
//
// Type values from CK_Warehouses_Type set: 'Main' | 'Satellite' |
// 'Branch'. IsActive=0 for 4 of 24 (WH-DM06, 20, 22, 24 — ~17%, near
// the brief's target). Mock UI's third "maintenance" intermediate state
// is intentionally absent — schema is bool-only (TD-009).
//
// Address populated for every row — gives the search field
// (Code/Name/Address) something to match against. PhoneNumber /
// ManagerName / TimeZoneId left NULL — those are admin-CRUD concerns
// for Phase 7+.
//
// Idempotent: NOT EXISTS guard per row, symmetric Down via DELETE-JOIN.
[Migration(20260508007L)]
[Tags("Tenant")]
public class Migration_20260508_007_SeedDemoWarehouses : MigrationBase
{
    private const string SeedValues = @"
    (VALUES
        ('WH-DM01', N'Bangkok Distribution Hub',        'Main',      N'Bangkok, TH',           1),
        ('WH-DM02', N'Chiang Mai Regional',             'Branch',    N'Chiang Mai, TH',        1),
        ('WH-DM03', N'Phuket Coastal Hub',              'Branch',    N'Phuket, TH',            1),
        ('WH-DM04', N'Khon Kaen East',                  'Satellite', N'Khon Kaen, TH',         1),
        ('WH-DM05', N'Pattaya Forward DC',              'Satellite', N'Pattaya, TH',           1),
        ('WH-DM06', N'Samut Prakan Cold (Decommissioned)', 'Satellite', N'Samut Prakan, TH',   0),
        ('WH-DM07', N'Hat Yai South',                   'Branch',    N'Hat Yai, TH',           1),
        ('WH-DM08', N'Korat Central',                   'Satellite', N'Nakhon Ratchasima, TH', 1),
        ('WH-DM09', N'Surat Thani Logistics',           'Branch',    N'Surat Thani, TH',       1),
        ('WH-DM10', N'Udon Thani Distribution',         'Satellite', N'Udon Thani, TH',        1),
        ('WH-DM11', N'Ayutthaya Manufacturing',         'Satellite', N'Ayutthaya, TH',         1),
        ('WH-DM12', N'Rayong Eastern Seaboard',         'Branch',    N'Rayong, TH',            1),
        ('WH-DM13', N'Kanchanaburi Bulk',               'Satellite', N'Kanchanaburi, TH',      1),
        ('WH-DM14', N'Chonburi Industrial',             'Branch',    N'Chonburi, TH',          1),
        ('WH-DM15', N'Bangkok Sukhumvit',               'Branch',    N'Bangkok, TH',           1),
        ('WH-DM16', N'Bangkok Lat Krabang',             'Satellite', N'Bangkok, TH',           1),
        ('WH-DM17', N'Bangkok Bang Na',                 'Satellite', N'Bangkok, TH',           1),
        ('WH-DM18', N'Trat Eastern Border',             'Satellite', N'Trat, TH',              1),
        ('WH-DM19', N'Chiang Rai Northern Border',      'Satellite', N'Chiang Rai, TH',        1),
        ('WH-DM20', N'Singapore Hub (Paused)',          'Main',      N'Jurong East, Singapore', 0),
        ('WH-DM21', N'Ho Chi Minh South VN',            'Branch',    N'Ho Chi Minh City, VN',  1),
        ('WH-DM22', N'Bangkok Cold Phase 1 (Closed)',   'Satellite', N'Bangkok, TH',           0),
        ('WH-DM23', N'Mae Sot Border Trade',            'Satellite', N'Mae Sot, TH',           1),
        ('WH-DM24', N'Krabi Tourist Logistics (Seasonal)', 'Satellite', N'Krabi, TH',          0)
    ) AS s (Code, Name, Type, Address, IsActive)";

    public override void Up() =>
        Execute.Sql($@"
;WITH seed AS (
    SELECT s.Code, s.Name, s.Type, s.Address, s.IsActive
    FROM {SeedValues}
)
INSERT INTO [master].[Warehouses]
    (Id, Code, Name, Type, Address, IsActive, CreatedAt)
SELECT NEWID(), s.Code, s.Name, s.Type, s.Address, s.IsActive, SYSUTCDATETIME()
FROM seed s
WHERE NOT EXISTS (
    SELECT 1 FROM [master].[Warehouses] w WHERE w.Code = s.Code
);");

    public override void Down() =>
        Execute.Sql($@"
;WITH seed AS (
    SELECT s.Code FROM {SeedValues}
)
DELETE w
FROM [master].[Warehouses] w
INNER JOIN seed s ON w.Code = s.Code;");
}
