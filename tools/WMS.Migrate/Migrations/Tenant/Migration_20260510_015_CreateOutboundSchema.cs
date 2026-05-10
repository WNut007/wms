using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14A — outbound schema for sales-order workflow + downstream
// surfaces (waves, picks, packs, shipments planned for 14B/C/D).
// Created here so future tables under the schema can reference each
// other without inter-migration ordering surprises.
//
// Distinct from inbound.* (PO + Receiving): orders coming OUT to
// customers vs orders coming IN from suppliers. The two never share
// tables; reference flows through master.* (Customers, Owners) and
// inventory.* (Stock).
[Migration(20260510015L)]
[Tags("Tenant")]
public class Migration_20260510_015_CreateOutboundSchema : MigrationBase
{
    public override void Up() =>
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'outbound') " +
            "EXEC('CREATE SCHEMA [outbound]');");

    public override void Down() =>
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'outbound') " +
            "DROP SCHEMA [outbound];");
}
