using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504033L)]
[Tags("Tenant")]
public class Migration_20260504_033_CreateSecuritySchema : MigrationBase
{
    public override void Up() =>
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'security') " +
            "EXEC('CREATE SCHEMA [security]');");

    // Drop only when the schema is empty so a partial rollback can't take
    // out tables that haven't been rolled back yet.
    public override void Down() =>
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'security') " +
            "AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID('security')) " +
            "EXEC('DROP SCHEMA [security]');");
}
