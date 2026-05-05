using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Wires audit FK constraints (CreatedBy / UpdatedBy) on every operational
// table in the tenant DB's master schema to security.Users(Id). 23 tables ×
// 2 FKs = 46 constraints.
//
// ON DELETE NO ACTION — SQL Server forbids multiple ON DELETE SET NULL paths
// from one child table to the same parent ("multiple cascade paths", err 1785),
// which is exactly what CreatedBy + UpdatedBy → security.Users triggers.
// NO ACTION blocks deletion of a User while audit rows still reference them;
// in practice users are soft-deleted (IsActive = false on security.Users),
// so this preserves audit history more strongly than SET NULL would.
//
// Intentionally SKIPPED:
//   - master.SystemSettings — config table, no FK wiring per design
//     (mirrors migration 020's skip list).
//   - All security.* tables (Users, Roles, Functions, RoleFunctionPermissions,
//     UserRoles, AuditLog) — self-FK within the security schema is awkward
//     (Users.CreatedBy → Users.Id chicken-and-egg for the very first row;
//     AuditLog is the audit-of-audit case). They retain bare UNIQUEIDENTIFIER
//     NULL columns until a separate decision is made.
[Migration(20260504045L)]
[Tags("Tenant")]
public class Migration_20260504_045_WireTenantAuditForeignKeys : MigrationBase
{
    // Every entry here is a master-schema table with CreatedBy + UpdatedBy
    // columns confirmed against the create-table migrations + the standardize
    // pass in 020. Adding/removing operational tables in the future means
    // updating this list (and adding a follow-up migration for new tables).
    private static readonly string[] Tables =
    {
        "Warehouses",
        "WarehouseDocks",
        "HolidayCalendar",
        "Zones",
        "Locations",
        "PackStations",
        "ContainerTypes",
        "BoxTypes",
        "Owners",
        "ProductCategories",
        "UnitsOfMeasure",
        "UomConversions",
        "Products",
        "ProductBarcodes",
        "ProductPackingConfigs",
        "ProductOwners",
        "Customers",
        "Carriers",
        "CarrierConfigs",
        "CarrierCoverage",
        "CarrierRates",
        "Channels",
        "OrderSources",
    };

    public override void Up()
    {
        foreach (var table in Tables)
        {
            AddFkIfMissing(table, "CreatedBy", $"FK_{table}_CreatedBy");
            AddFkIfMissing(table, "UpdatedBy", $"FK_{table}_UpdatedBy");
        }
    }

    public override void Down()
    {
        // Reverse order from Up() — symmetric rollback.
        for (var i = Tables.Length - 1; i >= 0; i--)
        {
            var table = Tables[i];
            DropFkIfExists(table, $"FK_{table}_UpdatedBy");
            DropFkIfExists(table, $"FK_{table}_CreatedBy");
        }
    }

    // Idempotent FK ADD: re-running the migration after a partial failure is
    // safe. FluentMigrator's Create.ForeignKey has no IF-NOT-EXISTS hook, so
    // raw SQL with sys.foreign_keys check is the standard escape hatch
    // (same pattern as migration 027).
    private void AddFkIfMissing(string table, string column, string fkName) =>
        Execute.Sql(
            $"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys " +
            $"WHERE name = '{fkName}' " +
            $"AND parent_object_id = OBJECT_ID('master.{table}')) " +
            $"BEGIN " +
            $"ALTER TABLE [master].[{table}] " +
            $"ADD CONSTRAINT {fkName} " +
            $"FOREIGN KEY ([{column}]) " +
            $"REFERENCES [security].[Users]([Id]) " +
            $"ON DELETE NO ACTION; " +
            $"END");

    private void DropFkIfExists(string table, string fkName) =>
        Execute.Sql(
            $"IF EXISTS (SELECT 1 FROM sys.foreign_keys " +
            $"WHERE name = '{fkName}' " +
            $"AND parent_object_id = OBJECT_ID('master.{table}')) " +
            $"ALTER TABLE [master].[{table}] DROP CONSTRAINT {fkName};");
}
