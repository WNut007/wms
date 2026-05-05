using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Closes TD-002: master.Customers.PreferredCarrierId was created as a logical
// FK before master.Carriers existed. Now that Carriers (021) and its children
// have landed, this migration enforces referential integrity.
//
// ON DELETE SET NULL — a customer's "preferred" carrier is a soft hint; if
// the carrier is hard-deleted we just clear the preference rather than
// cascade-delete the customer.
[Migration(20260504027L)]
[Tags("Tenant")]
public class Migration_20260504_027_AddCustomersCarrierForeignKey : MigrationBase
{
    // Idempotent FK ADD: re-running the migration after a partial failure
    // is safe. FluentMigrator's Create.ForeignKey has no IF-NOT-EXISTS hook,
    // so raw SQL with sys.foreign_keys check is the standard escape hatch.
    public override void Up() =>
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys " +
            "WHERE name = 'FK_Customers_Carrier' " +
            "AND parent_object_id = OBJECT_ID('master.Customers')) " +
            "BEGIN " +
            "ALTER TABLE [master].[Customers] " +
            "ADD CONSTRAINT FK_Customers_Carrier " +
            "FOREIGN KEY ([PreferredCarrierId]) " +
            "REFERENCES [master].[Carriers]([Id]) " +
            "ON DELETE SET NULL; " +
            "END");

    public override void Down() =>
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.foreign_keys " +
            "WHERE name = 'FK_Customers_Carrier' " +
            "AND parent_object_id = OBJECT_ID('master.Customers')) " +
            "ALTER TABLE [master].[Customers] DROP CONSTRAINT FK_Customers_Carrier;");
}
