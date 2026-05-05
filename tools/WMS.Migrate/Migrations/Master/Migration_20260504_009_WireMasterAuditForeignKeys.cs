using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

// Wires audit FK constraints (CreatedBy / UpdatedBy) on master DB tables to
// master.SuperAdmins(Id). Master DB has no security.Users — only super-admin
// operators touch the master DB, so SuperAdmins is the correct identity table.
//
// ON DELETE NO ACTION — SQL Server forbids multiple ON DELETE SET NULL paths
// from one child table to the same parent ("multiple cascade paths", err 1785),
// which is exactly what CreatedBy + UpdatedBy → SuperAdmins triggers. NO ACTION
// blocks deletion of a SuperAdmin while audit rows still reference them; in
// practice operators are soft-deleted (IsActive = false), so this preserves
// audit history more strongly than SET NULL would.
//
// Intentionally SKIPPED:
//   - master.SuperAdmins itself — self-FK would create a chicken-and-egg
//     bootstrap problem (the very first row has no prior author to point at).
//     CreatedBy / UpdatedBy stay as bare UNIQUEIDENTIFIER NULL.
//   - master.LoginAttempts, SystemAuditLog, PreAuthTokens — audit / transient
//     tables, no audit-of-audit (consistent with migration 008's skip list).
//   - master.SystemSettings — config table, no FK wiring per design.
[Migration(20260504009L)]
[Tags("Master")]
public class Migration_20260504_009_WireMasterAuditForeignKeys : MigrationBase
{
    public override void Up()
    {
        AddFkIfMissing("Tenants", "CreatedBy", "FK_Tenants_CreatedBy");
        AddFkIfMissing("Tenants", "UpdatedBy", "FK_Tenants_UpdatedBy");

        AddFkIfMissing("UserTenantMap", "CreatedBy", "FK_UserTenantMap_CreatedBy");
        AddFkIfMissing("UserTenantMap", "UpdatedBy", "FK_UserTenantMap_UpdatedBy");
    }

    public override void Down()
    {
        // Reverse order from Up() — symmetric rollback.
        DropFkIfExists("UserTenantMap", "FK_UserTenantMap_UpdatedBy");
        DropFkIfExists("UserTenantMap", "FK_UserTenantMap_CreatedBy");

        DropFkIfExists("Tenants", "FK_Tenants_UpdatedBy");
        DropFkIfExists("Tenants", "FK_Tenants_CreatedBy");
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
            $"REFERENCES [master].[SuperAdmins]([Id]) " +
            $"ON DELETE NO ACTION; " +
            $"END");

    private void DropFkIfExists(string table, string fkName) =>
        Execute.Sql(
            $"IF EXISTS (SELECT 1 FROM sys.foreign_keys " +
            $"WHERE name = '{fkName}' " +
            $"AND parent_object_id = OBJECT_ID('master.{table}')) " +
            $"ALTER TABLE [master].[{table}] DROP CONSTRAINT {fkName};");
}
