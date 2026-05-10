using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14E — widen CK_SalesOrders_Status to add 'Shipped' state.
// SQL Server doesn't allow CHECK widening in-place; drop and re-add
// (same pattern as Phase 14B's _018 + 14C's _021 + 14D's _025).
//
// MVP-after-14E set:
//   Draft, Open, Allocating, Allocated,
//   Picking, Picked, PartiallyPicked,
//   Packed,
//   Shipped,
//   Cancelled
//
// Shipped = terminal happy state. SO transitions Packed → Shipped on
// ShipmentService.SubmitAsync. No reversal in MVP — operator must use
// a (future) return-to-stock workflow if a shipped SO needs to be
// undone (TD-038 family).
[Migration(20260510029L)]
[Tags("Tenant")]
public class Migration_20260510_029_AlterSalesOrdersStatusEnumForShipped : MigrationBase
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
                "'Picking', 'Picked', 'PartiallyPicked', " +
                "'Packed', 'Shipped', " +
                "'Cancelled'));");
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
                "'Draft', 'Open', 'Allocating', 'Allocated', " +
                "'Picking', 'Picked', 'PartiallyPicked', " +
                "'Packed', " +
                "'Cancelled'));");
    }
}
