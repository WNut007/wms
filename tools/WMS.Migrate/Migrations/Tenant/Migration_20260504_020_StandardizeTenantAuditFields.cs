using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Tech debt #1: standardize audit fields across tenant master schema tables.
// Adds the missing UpdatedAt / CreatedBy / UpdatedBy columns where absent.
// CreatedAt is already present on every operational table, so this migration
// only fills the gaps.
//
// All added columns are NULLABLE — no DEFAULT constraint is created, so Down()
// can DROP COLUMN directly without first dropping a DF_ constraint.
//
// Intentionally SKIPPED:
//   - master.SystemSettings  (config table — already has UpdatedBy)
//
// FK to a future security.Users table is deliberately NOT added here — the
// security schema is not built yet. CreatedBy / UpdatedBy are stored as bare
// UNIQUEIDENTIFIER until that schema lands.
[Migration(20260504020L)]
[Tags("Tenant")]
public class Migration_20260504_020_StandardizeTenantAuditFields : MigrationBase
{
    public override void Up()
    {
        // master.BoxTypes — already has CreatedAt + UpdatedAt.
        AddColumnIfMissing("BoxTypes", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("BoxTypes", "UpdatedBy", "UNIQUEIDENTIFIER NULL");

        // master.ContainerTypes — already has CreatedAt + UpdatedAt.
        AddColumnIfMissing("ContainerTypes", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("ContainerTypes", "UpdatedBy", "UNIQUEIDENTIFIER NULL");

        // master.HolidayCalendar — has CreatedAt only.
        AddColumnIfMissing("HolidayCalendar", "UpdatedAt", "DATETIME2 NULL");
        AddColumnIfMissing("HolidayCalendar", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("HolidayCalendar", "UpdatedBy", "UNIQUEIDENTIFIER NULL");

        // master.Owners — already has CreatedAt + UpdatedAt.
        AddColumnIfMissing("Owners", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("Owners", "UpdatedBy", "UNIQUEIDENTIFIER NULL");

        // master.PackStations — already has CreatedAt + UpdatedAt.
        AddColumnIfMissing("PackStations", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("PackStations", "UpdatedBy", "UNIQUEIDENTIFIER NULL");

        // master.WarehouseDocks — has CreatedAt only.
        AddColumnIfMissing("WarehouseDocks", "UpdatedAt", "DATETIME2 NULL");
        AddColumnIfMissing("WarehouseDocks", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("WarehouseDocks", "UpdatedBy", "UNIQUEIDENTIFIER NULL");

        // master.Zones — already has CreatedAt + UpdatedAt.
        AddColumnIfMissing("Zones", "CreatedBy", "UNIQUEIDENTIFIER NULL");
        AddColumnIfMissing("Zones", "UpdatedBy", "UNIQUEIDENTIFIER NULL");
    }

    public override void Down()
    {
        // Reverse order from Up() — symmetric rollback.
        DropColumnIfExists("Zones", "UpdatedBy");
        DropColumnIfExists("Zones", "CreatedBy");

        DropColumnIfExists("WarehouseDocks", "UpdatedBy");
        DropColumnIfExists("WarehouseDocks", "CreatedBy");
        DropColumnIfExists("WarehouseDocks", "UpdatedAt");

        DropColumnIfExists("PackStations", "UpdatedBy");
        DropColumnIfExists("PackStations", "CreatedBy");

        DropColumnIfExists("Owners", "UpdatedBy");
        DropColumnIfExists("Owners", "CreatedBy");

        DropColumnIfExists("HolidayCalendar", "UpdatedBy");
        DropColumnIfExists("HolidayCalendar", "CreatedBy");
        DropColumnIfExists("HolidayCalendar", "UpdatedAt");

        DropColumnIfExists("ContainerTypes", "UpdatedBy");
        DropColumnIfExists("ContainerTypes", "CreatedBy");

        DropColumnIfExists("BoxTypes", "UpdatedBy");
        DropColumnIfExists("BoxTypes", "CreatedBy");
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
