using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// inbound.PurchaseOrderLines — one row per ordered (Product, UoM)
// within a PO. Receipts (Receiving-δ) bump ReceivedQuantity and flip
// Status from 'Open' to 'PartiallyReceived' to 'Closed' as the line
// fills up.
//
// Same product on multiple lines is allowed — common when ordering
// distinct lots / dates of the same SKU. Uniqueness is on
// (PurchaseOrderId, LineNumber), not on ProductId.
//
// FK CreatedBy / UpdatedBy → security.Users is wired in migration 056.
[Migration(20260504055L)]
[Tags("Tenant")]
public class Migration_20260504_055_CreatePurchaseOrderLinesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("PurchaseOrderLines").InSchema("inbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // CASCADE on delete: lines belong to the PO. Cancelling /
            // deleting the PO takes its lines with it. POs themselves
            // don't really get hard-deleted in production; this is a
            // dev/admin convenience.
            .WithColumn("PurchaseOrderId").AsGuid().NotNullable()
                .ForeignKey("FK_PurchaseOrderLines_PurchaseOrder",
                            "inbound", "PurchaseOrders", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("LineNumber").AsInt32().NotNullable()

            // FKs to master tables — NO ACTION (default) preserves stock
            // / order traceability when a Product or UoM is retired.
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_PurchaseOrderLines_Product", "master", "Products", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_PurchaseOrderLines_Uom", "master", "UnitsOfMeasure", "Id")

            // DECIMAL(18,4) matches inventory.Stock.QuantityOnHand so
            // the receive flow doesn't have to reason about precision.
            .WithColumn("ExpectedQuantity").AsDecimal(18, 4).NotNullable()
            .WithColumn("ReceivedQuantity").AsDecimal(18, 4).NotNullable()
                .WithDefaultValue(0)

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Open")

            // Audit + optimistic concurrency.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable()
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Per-PO LineNumber uniqueness — doubles as the ordering index
        // for displaying lines in their original sequence.
        Create.Index("UX_PurchaseOrderLines_Po_LineNumber")
            .OnTable("PurchaseOrderLines").InSchema("inbound")
            .WithOptions().Unique()
            .OnColumn("PurchaseOrderId").Ascending()
            .OnColumn("LineNumber").Ascending();

        // Per-product open-line lookup — "what's on order for SKU X?".
        Create.Index("IX_PurchaseOrderLines_Product")
            .OnTable("PurchaseOrderLines").InSchema("inbound")
            .OnColumn("ProductId").Ascending()
            .OnColumn("Status").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [inbound].[PurchaseOrderLines] " +
            "ADD CONSTRAINT CK_PurchaseOrderLines_Status " +
            "CHECK (Status IN ('Open', 'PartiallyReceived', 'Closed', 'Cancelled'));");

        // Quantity invariants — non-positive expected qty is a data bug
        // (use Cancelled status to retire a line); negative received is
        // never valid.
        Execute.Sql(
            "ALTER TABLE [inbound].[PurchaseOrderLines] " +
            "ADD CONSTRAINT CK_PurchaseOrderLines_ExpectedQty_Positive " +
            "CHECK (ExpectedQuantity > 0);");

        Execute.Sql(
            "ALTER TABLE [inbound].[PurchaseOrderLines] " +
            "ADD CONSTRAINT CK_PurchaseOrderLines_ReceivedQty_NonNegative " +
            "CHECK (ReceivedQuantity >= 0);");
    }

    public override void Down() =>
        Delete.Table("PurchaseOrderLines").InSchema("inbound");
}
