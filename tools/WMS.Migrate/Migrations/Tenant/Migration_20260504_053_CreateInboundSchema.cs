using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// CREATE SCHEMA must be the only statement in its batch — wrap in
// EXEC so it can ride alongside FluentMigrator's transaction.
[Migration(20260504053L)]
[Tags("Tenant")]
public class Migration_20260504_053_CreateInboundSchema : MigrationBase
{
    public override void Up() =>
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'inbound') " +
            "EXEC('CREATE SCHEMA [inbound]');");

    public override void Down() =>
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'inbound') " +
            "DROP SCHEMA [inbound];");
}
