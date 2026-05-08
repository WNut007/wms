using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 6B (Q2): mock UI displayed a Country facet (filter chip + Detail
// row) that the schema didn't carry. Persist it as a flat string rather
// than normalising into a Countries table — a 2-letter ISO code would be
// the principled call, but Customer.Country is a display-only field for
// Phase 6B and free-text gives us migration headroom (English/Thai/local
// names) without churning seed data when ISO conversion lands later.
//
// NULLABLE: most existing customers (none yet besides what 006 will seed)
// and historical imports won't have country reliably populated.
//
// NVARCHAR(100): generous — covers full localised country names.
//
// Idempotent ADD COLUMN — same pattern as migration 003 / 020.
[Migration(20260508004L)]
[Tags("Tenant")]
public class Migration_20260508_004_AddCustomerCountryColumn : MigrationBase
{
    public override void Up() =>
        AddColumnIfMissing("Customers", "Country", "NVARCHAR(100) NULL");

    public override void Down() =>
        DropColumnIfExists("Customers", "Country");

    private void AddColumnIfMissing(string table, string column, string typeSpec) =>
        Execute.Sql(
            $"IF NOT EXISTS (SELECT 1 FROM sys.columns " +
            $"WHERE object_id = OBJECT_ID('master.{table}') AND name = '{column}') " +
            $"BEGIN ALTER TABLE [master].[{table}] ADD [{column}] {typeSpec}; END");

    private void DropColumnIfExists(string table, string column) =>
        Execute.Sql(
            $"IF EXISTS (SELECT 1 FROM sys.columns " +
            $"WHERE object_id = OBJECT_ID('master.{table}') AND name = '{column}') " +
            $"BEGIN ALTER TABLE [master].[{table}] DROP COLUMN [{column}]; END");
}
