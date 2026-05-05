using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Seeds the four canonical Thai carriers every tenant starts with.
// All rows land with Status = 'Inactive' — the admin must walk each
// carrier through Configured → Tested → Production before live routing.
// IsActive = 1 keeps the rows visible in the admin UI from day one.
//
// Idempotent: existence-checked by Code (the unique key), so re-running
// after a partial failure or after a row was already inserted manually
// is safe. No WebsiteUrl / LogoUrl seeded — admin fills those in to
// avoid baking external URLs into migrations.
[Migration(20260504028L)]
[Tags("Tenant")]
public class Migration_20260504_028_SeedDefaultCarriers : MigrationBase
{
    private static readonly (string Code, string Name, string Type, int DisplayOrder)[] Carriers =
    {
        ("FLASH",    "Flash Express",  "Express", 1),
        ("KERRY",    "Kerry Express",  "Express", 2),
        ("JT",       "J&T Express",    "Express", 3),
        ("THAIPOST", "Thailand Post",  "Postal",  4),
    };

    public override void Up()
    {
        foreach (var (code, name, type, displayOrder) in Carriers)
        {
            // N-prefix on Name keeps the unicode literal intact through the
            // SQL Server parser. Code/Type/Country are ASCII so plain quotes
            // are fine. SYSUTCDATETIME() matches the table's CreatedAt
            // default precision (DATETIME2) — using the table default would
            // be cleaner, but we set it explicitly to keep the seed payload
            // self-documenting.
            Execute.Sql(
                $"IF NOT EXISTS (SELECT 1 FROM [master].[Carriers] WHERE Code = '{code}') " +
                $"INSERT INTO [master].[Carriers] " +
                $"(Id, Code, Name, Type, Country, Status, DisplayOrder, IsActive, CreatedAt) " +
                $"VALUES (NEWID(), '{code}', N'{name}', '{type}', 'TH', 'Inactive', " +
                $"{displayOrder}, 1, SYSUTCDATETIME());");
        }
    }

    public override void Down() =>
        // Hard delete by Code — CASCADE on CarrierConfigs/Coverage/Rates
        // sweeps any operator-added config along with the row, and
        // SET NULL on Customers.PreferredCarrierId clears soft references.
        // Rollback intentionally resets the carrier slate.
        Execute.Sql(
            "DELETE FROM [master].[Carriers] " +
            "WHERE Code IN ('FLASH', 'KERRY', 'JT', 'THAIPOST');");
}
