using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Wires audit FK constraints (CreatedBy / UpdatedBy) on the receiving
// tables to security.Users(Id), mirroring 045 / 051 / 056. NO ACTION
// on delete — see CLAUDE.md "Audit Field FK Rules".
[Migration(20260504059L)]
[Tags("Tenant")]
public class Migration_20260504_059_WireReceivingAuditForeignKeys : MigrationBase
{
    private static readonly string[] Tables =
    {
        "ReceivingHeaders",
        "ReceivingLines",
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
        for (var i = Tables.Length - 1; i >= 0; i--)
        {
            var table = Tables[i];
            DropFkIfExists(table, $"FK_{table}_UpdatedBy");
            DropFkIfExists(table, $"FK_{table}_CreatedBy");
        }
    }

    private void AddFkIfMissing(string table, string column, string fkName) =>
        Execute.Sql(
            $"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys " +
            $"WHERE name = '{fkName}' " +
            $"AND parent_object_id = OBJECT_ID('inbound.{table}')) " +
            $"BEGIN " +
            $"ALTER TABLE [inbound].[{table}] " +
            $"ADD CONSTRAINT {fkName} " +
            $"FOREIGN KEY ([{column}]) " +
            $"REFERENCES [security].[Users]([Id]) " +
            $"ON DELETE NO ACTION; " +
            $"END");

    private void DropFkIfExists(string table, string fkName) =>
        Execute.Sql(
            $"IF EXISTS (SELECT 1 FROM sys.foreign_keys " +
            $"WHERE name = '{fkName}' " +
            $"AND parent_object_id = OBJECT_ID('inbound.{table}')) " +
            $"ALTER TABLE [inbound].[{table}] DROP CONSTRAINT {fkName};");
}
