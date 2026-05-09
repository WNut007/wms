using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 12 — counts schema for cycle-count workflows. Distinct from
// inventory.Adjustments (Phase 11A — single-line manual adjustments)
// per ADR-013 separation of concerns: counts.* is for periodic stock
// reconciliation, inventory.Adjustments for ad-hoc operator-driven
// corrections. The two share Movement Log integration but live in
// separate tables so reporting can distinguish "scheduled reconcile"
// from "exception write-off".
[Migration(20260510010L)]
[Tags("Tenant")]
public class Migration_20260510_010_CreateCountsSchema : MigrationBase
{
    public override void Up() =>
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'counts') " +
            "EXEC('CREATE SCHEMA [counts]');");

    public override void Down() =>
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'counts') " +
            "DROP SCHEMA [counts];");
}
