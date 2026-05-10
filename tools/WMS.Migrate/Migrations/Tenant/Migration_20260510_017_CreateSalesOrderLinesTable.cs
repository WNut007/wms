using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14A — outbound.SalesOrderLines. One row per ordered
// (Product, Owner, UoM) within an SO. Same product on multiple lines
// is allowed — common when a customer orders both Owner-A's stock
// and Owner-B's stock of the same SKU through the same channel.
// Uniqueness is on (SalesOrderId, LineNumber), not on ProductId.
//
// Owner preserved per ADR-007 (3PL/VMI invariant). When 14B's
// allocation logic runs, it allocates from Stock matching the
// (Product, Owner, UoM) on the line — preventing accidental
// commingling of one customer's owner-keyed stock with another's.
//
// Allocation/pick/ship qty columns deliberately OMITTED from MVP.
// Phase 14B will ALTER TABLE to add AllocatedQty / PickedQty / etc.
// when the workflow needs them. Pre-seating those columns now would
// invite incorrect schema decisions before the use case is real.
//
// CreatedBy / UpdatedBy FK to security.Users wired inline (Phase
// 11A/12/13 precedent).
[Migration(20260510017L)]
[Tags("Tenant")]
public class Migration_20260510_017_CreateSalesOrderLinesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("SalesOrderLines").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // CASCADE on delete: lines belong to the SO. Cancelling /
            // hard-deleting an SO takes its lines with it. Production
            // SOs typically aren't hard-deleted (Cancel is the usual
            // path); this is a dev/admin convenience.
            .WithColumn("SalesOrderId").AsGuid().NotNullable()
                .ForeignKey("FK_SalesOrderLines_SalesOrder",
                            "outbound", "SalesOrders", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("LineNumber").AsInt32().NotNullable()

            // Owner preserved per ADR-007 (3PL/VMI invariant). NOT NULL —
            // every line knows whose stock to draw against. No FK to
            // master.Owners.OwnerType: Service-level validation handles
            // type rules (mirrors PurchaseOrderLines pattern).
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_SalesOrderLines_Product", "master", "Products", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_SalesOrderLines_Owner", "master", "Owners", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_SalesOrderLines_Uom", "master", "UnitsOfMeasure", "Id")

            // DECIMAL(18,4) matches inventory.Stock.QuantityOnHand so
            // allocation in 14B can compare without precision drift.
            .WithColumn("OrderedQuantity").AsDecimal(18, 4).NotNullable()

            // Optional unit price — captured at order entry for the
            // pack slip / invoice. Customer-tier pricing logic will
            // consume this in a later phase; for MVP it's free-text.
            .WithColumn("UnitPrice").AsDecimal(18, 4).Nullable()

            .WithColumn("Notes").AsString(500).Nullable()

            // Audit + optimistic concurrency. CreatedBy/UpdatedBy FK inline.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_SalesOrderLines_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_SalesOrderLines_UpdatedBy", "security", "Users", "Id")
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Per-SO LineNumber uniqueness — doubles as the ordering index
        // for displaying lines in their original sequence.
        Create.Index("UX_SalesOrderLines_So_LineNumber")
            .OnTable("SalesOrderLines").InSchema("outbound")
            .WithOptions().Unique()
            .OnColumn("SalesOrderId").Ascending()
            .OnColumn("LineNumber").Ascending();

        // Per-product open-line lookup — "what's on order for SKU X
        // across all customers?". Foundation for Phase 14B allocation
        // queries.
        Create.Index("IX_SalesOrderLines_Product")
            .OnTable("SalesOrderLines").InSchema("outbound")
            .OnColumn("ProductId").Ascending();

        // Quantity invariant: zero-qty lines are a data bug. Use the
        // Cancelled status on the parent SO (or DELETE the line on
        // Drafts) to retire one.
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "ADD CONSTRAINT CK_SalesOrderLines_OrderedQty_Positive " +
            "CHECK (OrderedQuantity > 0);");

        // UnitPrice invariant: NULL or non-negative. Negative prices
        // would imply credit notes, which are a separate workflow
        // (deferred — TD-XXX in 14B).
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "ADD CONSTRAINT CK_SalesOrderLines_UnitPrice_NonNegative " +
            "CHECK (UnitPrice IS NULL OR UnitPrice >= 0);");
    }

    public override void Down() =>
        Delete.Table("SalesOrderLines").InSchema("outbound");
}
