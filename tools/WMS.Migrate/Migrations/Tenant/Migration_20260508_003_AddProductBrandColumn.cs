using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 6B (Q2): mock UI exposed a Brand chip that the schema didn't carry.
// Rather than drop the field, persist it — Brand is a real merchandising
// dimension (filter facet, search hit) and small enough to keep on the
// Product row instead of normalising into a Brands table.
//
// NULLABLE on purpose:
//  - existing rows (DEMO-001) predate this column and stay unset
//  - some catalog entries genuinely have no brand (private label, generic)
//
// NVARCHAR(100): brand names are user-facing and may include non-ASCII
// (Thai, Korean, etc.); 100 chars matches Customer.CompanyName headroom.
//
// Idempotent ADD COLUMN — re-running the migration on a partially-applied
// database is safe (mirrors the pattern from migration 020).
[Migration(20260508003L)]
[Tags("Tenant")]
public class Migration_20260508_003_AddProductBrandColumn : MigrationBase
{
    public override void Up() =>
        AddColumnIfMissing("Products", "Brand", "NVARCHAR(100) NULL");

    public override void Down() =>
        DropColumnIfExists("Products", "Brand");

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
