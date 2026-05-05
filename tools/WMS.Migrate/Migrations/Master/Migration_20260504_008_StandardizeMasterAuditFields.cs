using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

// Tech debt #1: standardize audit fields across master schema tables.
// Adds the missing UpdatedAt / CreatedBy / UpdatedBy columns where absent.
// CreatedAt is already present on every operational table, so this migration
// only fills the gaps.
//
// All added columns are NULLABLE — no DEFAULT constraint is created, so Down()
// can DROP COLUMN directly without first dropping a DF_ constraint.
//
// Intentionally SKIPPED:
//   - master.LoginAttempts   (audit log table — no audit-of-audit)
//   - master.SystemAuditLog  (audit log table — no audit-of-audit)
//   - master.PreAuthTokens   (transient, has its own tracking columns)
//
// FK to a future security.Users table is deliberately NOT added here — the
// security schema is not built yet. CreatedBy / UpdatedBy are stored as bare
// UNIQUEIDENTIFIER until that schema lands.
[Migration(20260504008L)]
[Tags("Master")]
public class Migration_20260504_008_StandardizeMasterAuditFields : MigrationBase
{
    public override void Up()
    {
        // master.SuperAdmins — has CreatedAt only.
        AddColumnIfMissing("SuperAdmins", "UpdatedAt", "DATETIME2 NULL");
        AddColumnIfMissing("SuperAdmins", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("SuperAdmins", "UpdatedBy", "UNIQUEIDENTIFIER NULL");

        // master.Tenants — already has CreatedAt + UpdatedAt.
        AddColumnIfMissing("Tenants", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("Tenants", "UpdatedBy", "UNIQUEIDENTIFIER NULL");

        // master.UserTenantMap — has CreatedAt only.
        AddColumnIfMissing("UserTenantMap", "UpdatedAt", "DATETIME2 NULL");
        AddColumnIfMissing("UserTenantMap", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("UserTenantMap", "UpdatedBy", "UNIQUEIDENTIFIER NULL");
    }

    public override void Down()
    {
        // Reverse order from Up() — symmetric rollback.
        DropColumnIfExists("UserTenantMap", "UpdatedBy");
        DropColumnIfExists("UserTenantMap", "CreatedBy");
        DropColumnIfExists("UserTenantMap", "UpdatedAt");

        DropColumnIfExists("Tenants", "UpdatedBy");
        DropColumnIfExists("Tenants", "CreatedBy");

        DropColumnIfExists("SuperAdmins", "UpdatedBy");
        DropColumnIfExists("SuperAdmins", "CreatedBy");
        DropColumnIfExists("SuperAdmins", "UpdatedAt");
    }

    // Idempotent column-add: re-running the migration on a partially-applied
    // database is safe.
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
