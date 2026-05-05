using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504001L)]
[Tags("Tenant")]
public class Migration_20260504_001_CreateMasterSchema : MigrationBase
{
    public override void Up() =>
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'master') " +
            "EXEC('CREATE SCHEMA [master]');");

    // Drop only when the schema is empty so a partial rollback can't take
    // out tables that haven't been rolled back yet.
    public override void Down() =>
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'master') " +
            "AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID('master')) " +
            "EXEC('DROP SCHEMA [master]');");
}
