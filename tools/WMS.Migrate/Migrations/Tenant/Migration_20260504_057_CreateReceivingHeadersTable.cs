using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// inbound.ReceivingHeaders — one row per physical receipt event (one
// truck / one delivery). Multiple receipts can land against the same
// PO (split deliveries).
//
// Status:
//   Draft     — being entered, not yet posted (Phase 2 workflow)
//   Posted    — stock + PO line bumps applied (Phase 1 default)
//   Cancelled — rolled back / never applied (Phase 2)
//
// PurchaseOrderId is nullable to support blind receipts — goods that
// arrive without a matching PO (returns, transfers, supplier walk-ins).
//
// FK CreatedBy / UpdatedBy → security.Users wired in migration 059.
[Migration(20260504057L)]
[Tags("Tenant")]
public class Migration_20260504_057_CreateReceivingHeadersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("ReceivingHeaders").InSchema("inbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Tenant-wide unique receipt number — printed on the GRN
            // (Goods Received Note) and scanned during reconciliation.
            .WithColumn("ReceivingNumber").AsAnsiString(50).NotNullable().Unique()

            // Optional FK — blind receipts pass NULL.
            .WithColumn("PurchaseOrderId").AsGuid().Nullable()
                .ForeignKey("FK_ReceivingHeaders_PurchaseOrder",
                            "inbound", "PurchaseOrders", "Id")

            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_ReceivingHeaders_Warehouse",
                            "master", "Warehouses", "Id")

            // Defaults to "now" — operators rarely back-date receipts.
            .WithColumn("ReceivedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Posted")

            .WithColumn("Notes").AsString(1000).Nullable()

            // Audit + optimistic concurrency.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable()
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // "All receipts against PO X" — primary read for vendor
        // reconciliation screens.
        Create.Index("IX_ReceivingHeaders_PurchaseOrder")
            .OnTable("ReceivingHeaders").InSchema("inbound")
            .OnColumn("PurchaseOrderId").Ascending();

        // Per-warehouse receiving dashboard, latest first.
        Create.Index("IX_ReceivingHeaders_Warehouse")
            .OnTable("ReceivingHeaders").InSchema("inbound")
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("ReceivedAt").Descending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [inbound].[ReceivingHeaders] " +
            "ADD CONSTRAINT CK_ReceivingHeaders_Status " +
            "CHECK (Status IN ('Draft', 'Posted', 'Cancelled'));");
    }

    public override void Down() =>
        Delete.Table("ReceivingHeaders").InSchema("inbound");
}
