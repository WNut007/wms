using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// inbound.PurchaseOrders — the order header. Status starts at 'Open'
// (Phase 1 skips a separate Draft state); transitions to 'Receiving'
// once at least one line has been partially received and to 'Closed'
// when every line is fully received. 'Cancelled' is a terminal state.
//
// Owner is the supplier we're ordering FROM. The CHECK on Owner.Type
// (Supplier / VMI) is intentionally NOT enforced at the DB so this
// table doesn't depend on the Owners table's CHECK constraint;
// service-level validation handles the type rule.
//
// FK CreatedBy / UpdatedBy → security.Users is wired in migration 056
// alongside PurchaseOrderLines.
[Migration(20260504054L)]
[Tags("Tenant")]
public class Migration_20260504_054_CreatePurchaseOrdersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("PurchaseOrders").InSchema("inbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Tenant-wide unique number — printed on the supplier's
            // delivery note and scanned at receive time.
            .WithColumn("PoNumber").AsAnsiString(50).NotNullable().Unique()

            // FK → master.Owners (the supplier). NO ACTION on delete:
            // a supplier with PO history mustn't disappear silently —
            // soft-delete via IsActive on Owners instead.
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_PurchaseOrders_Owner", "master", "Owners", "Id")

            // Destination warehouse — receiving validates the receipt
            // happens here. Required even with single-warehouse tenants
            // so the column is unambiguous when more warehouses appear.
            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_PurchaseOrders_Warehouse", "master", "Warehouses", "Id")

            // Free-form delivery target date — nullable because not
            // every supplier commits to one.
            .WithColumn("ExpectedDate").AsDate().Nullable()

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Open")

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

        // Status-led lookups ("all open POs ordered by ExpectedDate")
        // are the dashboard's primary read.
        Create.Index("IX_PurchaseOrders_Status")
            .OnTable("PurchaseOrders").InSchema("inbound")
            .OnColumn("Status").Ascending()
            .OnColumn("ExpectedDate").Ascending();

        // Per-supplier history — useful for vendor performance reports.
        Create.Index("IX_PurchaseOrders_Owner")
            .OnTable("PurchaseOrders").InSchema("inbound")
            .OnColumn("OwnerId").Ascending();

        // Per-warehouse view — receiving teams filter to "their" POs.
        Create.Index("IX_PurchaseOrders_Warehouse")
            .OnTable("PurchaseOrders").InSchema("inbound")
            .OnColumn("WarehouseId").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [inbound].[PurchaseOrders] " +
            "ADD CONSTRAINT CK_PurchaseOrders_Status " +
            "CHECK (Status IN ('Open', 'Receiving', 'Closed', 'Cancelled'));");
    }

    public override void Down() =>
        Delete.Table("PurchaseOrders").InSchema("inbound");
}
