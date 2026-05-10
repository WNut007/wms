using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14C — widen CK_SalesOrders_Status to add Picking states.
// SQL Server doesn't allow CHECK widening in-place; drop and re-add
// with the expanded set.
//
// MVP-after-14C set:
//   Draft, Open, Allocating, Allocated, Picking, Picked,
//   PartiallyPicked, Cancelled
//
// Picking         = pick task generated, operator entering quantities
// Picked          = all lines fully picked (sum(PickedQty)==OrderedQty
//                   per line)
// PartiallyPicked = pick task submitted but some line short on stock
//                   (sum(PickedQty) < OrderedQty for at least one line)
//
// Future widening (14D): Packing, Packed, Shipping, Shipped, Closed.
[Migration(20260510021L)]
[Tags("Tenant")]
public class Migration_20260510_021_AlterSalesOrdersStatusEnumForPicking : MigrationBase
{
    public override void Up()
    {
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "DROP CONSTRAINT CK_SalesOrders_Status;");

        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "ADD CONSTRAINT CK_SalesOrders_Status " +
            "CHECK (Status IN (" +
                "'Draft', 'Open', 'Allocating', 'Allocated', " +
                "'Picking', 'Picked', 'PartiallyPicked', 'Cancelled'));");
    }

    public override void Down()
    {
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "DROP CONSTRAINT CK_SalesOrders_Status;");

        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "ADD CONSTRAINT CK_SalesOrders_Status " +
            "CHECK (Status IN (" +
                "'Draft', 'Open', 'Allocating', 'Allocated', 'Cancelled'));");
    }
}
