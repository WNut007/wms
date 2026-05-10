using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14A — outbound.SalesOrders header. MVP foundation: header +
// lines surface only (no allocation / pick / pack / ship yet — those
// land in 14B/C/D).
//
// Status flow (MVP, trimmed):
//   Draft → Open                                   (terminal happy)
//   Draft|Open → Cancelled                         (terminal failure)
//
// Future phases will add states between Open and the terminal happy
// path (Allocating, Allocated, Picking, Picked, Packed, Shipped,
// Closed). The CK below leaves room for those by allowing only the
// MVP set today; expanding it is a single ALTER in 14B.
//
// Owner is captured per LINE (3PL/VMI invariant per ADR-007), not on
// the header — a single SO can mix owner-keyed stock when the customer
// orders from multiple suppliers' inventory through the same channel.
//
// CreatedBy / UpdatedBy FK to security.Users wired inline (Phase
// 11A/12/13 precedent — separate FK-wiring migration was abandoned
// post-Phase 9).
[Migration(20260510016L)]
[Tags("Tenant")]
public class Migration_20260510_016_CreateSalesOrdersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("SalesOrders").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Tenant-wide unique number — printed on pick lists, pack
            // slips, customer-facing docs. Format: SO-YYYYMMDD-NNNN.
            .WithColumn("SoNumber").AsAnsiString(50).NotNullable().Unique()

            // FK → master.Customers. NO ACTION on delete: a customer
            // with order history mustn't disappear silently — soft-delete
            // via Status='Inactive' on Customers instead.
            .WithColumn("CustomerId").AsGuid().NotNullable()
                .ForeignKey("FK_SalesOrders_Customer", "master", "Customers", "Id")

            // Originating warehouse — fulfillment will pick from this
            // warehouse's stock. Required even with single-warehouse
            // tenants so future multi-warehouse moves don't ambiguate.
            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_SalesOrders_Warehouse", "master", "Warehouses", "Id")

            .WithColumn("OrderDate").AsDate().NotNullable()
                .WithDefaultValue(RawSql.Insert("CAST(SYSUTCDATETIME() AS DATE)"))

            // Customer's requested ship date — nullable since not every
            // order specifies one.
            .WithColumn("RequestedShipDate").AsDate().Nullable()

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Draft")

            .WithColumn("Notes").AsString(1000).Nullable()

            // Audit + optimistic concurrency. CreatedBy/UpdatedBy FK
            // inline.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_SalesOrders_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_SalesOrders_UpdatedBy", "security", "Users", "Id")
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Status-led lookups dominate the dashboard ("all open SOs
        // ordered by RequestedShipDate"). Status leads the index;
        // OrderDate DESC ties for stable display order.
        Create.Index("IX_SalesOrders_Status")
            .OnTable("SalesOrders").InSchema("outbound")
            .OnColumn("Status").Ascending()
            .OnColumn("OrderDate").Descending();

        // Per-customer history — useful for the Customer Detail page's
        // future Activity tab (TD-014 customer half).
        Create.Index("IX_SalesOrders_Customer")
            .OnTable("SalesOrders").InSchema("outbound")
            .OnColumn("CustomerId").Ascending()
            .OnColumn("OrderDate").Descending();

        // Per-warehouse view — fulfillment teams filter to "their" SOs.
        Create.Index("IX_SalesOrders_Warehouse")
            .OnTable("SalesOrders").InSchema("outbound")
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("Status").Ascending();

        // Status enum guard. Phase 14B+ adds more states via ALTER.
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "ADD CONSTRAINT CK_SalesOrders_Status " +
            "CHECK (Status IN ('Draft', 'Open', 'Cancelled'));");
    }

    public override void Down() =>
        Delete.Table("SalesOrders").InSchema("outbound");
}
