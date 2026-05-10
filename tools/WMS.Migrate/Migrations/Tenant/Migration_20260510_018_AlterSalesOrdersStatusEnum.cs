using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14B — widen CK_SalesOrders_Status to include the allocation
// states. SQL Server doesn't allow CHECK widening in-place; drop and
// re-add with the expanded set.
//
// MVP-after-14B set:
//   Draft, Open, Allocating, Allocated, Cancelled
//
// Allocating = AllocateAsync started but couldn't fully fulfill (some
// lines short on stock); operator can re-run to pick up new arrivals
// or cancel the SO and release the partial reservations.
// Allocated   = all lines fully allocated; ready for pick (14C).
//
// Future widening (14C/D): Picking, Picked, Packed, Shipped, Closed.
[Migration(20260510018L)]
[Tags("Tenant")]
public class Migration_20260510_018_AlterSalesOrdersStatusEnum : MigrationBase
{
    public override void Up()
    {
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "DROP CONSTRAINT CK_SalesOrders_Status;");

        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "ADD CONSTRAINT CK_SalesOrders_Status " +
            "CHECK (Status IN ('Draft', 'Open', 'Allocating', 'Allocated', 'Cancelled'));");
    }

    public override void Down()
    {
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "DROP CONSTRAINT CK_SalesOrders_Status;");

        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrders] " +
            "ADD CONSTRAINT CK_SalesOrders_Status " +
            "CHECK (Status IN ('Draft', 'Open', 'Cancelled'));");
    }
}
