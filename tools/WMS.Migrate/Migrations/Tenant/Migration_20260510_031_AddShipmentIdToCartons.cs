using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14E — link existing outbound.Cartons rows back to the
// Shipment that dispatched them. Nullable: pre-Submit shipments
// haven't claimed their cartons yet, and pre-14E cartons have no
// shipment to link to.
//
// On ShipmentService.SubmitAsync the service stamps every Carton
// belonging to the SO (resolved via PackTask.SalesOrderId) with the
// new ShipmentId inside the same TX as the header flip.
//
// FK NO ACTION (default) — Shipment rows live forever once shipped;
// hard-deleting one would orphan its cartons, which the FK prevents.
[Migration(20260510031L)]
[Tags("Tenant")]
public class Migration_20260510_031_AddShipmentIdToCartons : MigrationBase
{
    public override void Up()
    {
        Alter.Table("Cartons").InSchema("outbound")
            .AddColumn("ShipmentId").AsGuid().Nullable()
                .ForeignKey("FK_Cartons_Shipment",
                            "outbound", "Shipments", "Id");

        // Filtered index — most cartons are in flight (ShipmentId NULL);
        // only ship-side queries care about the populated rows. Skipping
        // the NULLs keeps the index small.
        Execute.Sql(@"
CREATE INDEX IX_Cartons_Shipment
    ON [outbound].[Cartons] (ShipmentId)
    WHERE ShipmentId IS NOT NULL;");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IX_Cartons_Shipment ON [outbound].[Cartons];");
        Delete.ForeignKey("FK_Cartons_Shipment").OnTable("Cartons").InSchema("outbound");
        Delete.Column("ShipmentId").FromTable("Cartons").InSchema("outbound");
    }
}
