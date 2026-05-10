using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14D — widen CK_SalesOrders_Status to add 'Packed' state.
// SQL Server doesn't allow CHECK widening in-place; drop and re-add
// with the expanded set (same pattern as Phase 14B's _018 + 14C's _021).
//
// MVP-after-14D set:
//   Draft, Open, Allocating, Allocated,
//   Picking, Picked, PartiallyPicked,
//   Packed,
//   Cancelled
//
// Packed = single terminal post-pack state for MVP. PartiallyPicked
//          SOs can also pack what they got (operator just doesn't fill
//          the missing portion; SO transitions PartiallyPicked → Packed
//          on submit).
//
// No 'Packing' intermediate state for MVP — pack workflow is single-
// shot ("operator opens task, fills carton, submits"). PackTask itself
// has Pending → Packed | Cancelled internally; the SO header doesn't
// need to mirror that.
//
// Future widening (14F): Shipping, Shipped, Closed.
[Migration(20260510025L)]
[Tags("Tenant")]
public class Migration_20260510_025_AlterSalesOrdersStatusEnumForPacked : MigrationBase
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
                "'Packed', " +
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
                "'Picking', 'Picked', 'PartiallyPicked', 'Cancelled'));");
    }
}
