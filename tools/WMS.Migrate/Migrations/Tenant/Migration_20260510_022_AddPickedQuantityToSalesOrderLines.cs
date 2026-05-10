using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14C — denormalized aggregate of pick-line qty actually picked
// for the SO line. Stays in sync inside the same TX as SubmitAsync.
//
// Constraint: 0 <= PickedQuantity <= OrderedQuantity. Note that
// PickedQuantity is bounded by Ordered, NOT by Allocated — short-pick
// is real (operator picks less than allocated; AllocatedQuantity
// decrements on pick to release the unfilled reservation), but you
// can never pick more than originally ordered.
[Migration(20260510022L)]
[Tags("Tenant")]
public class Migration_20260510_022_AddPickedQuantityToSalesOrderLines : MigrationBase
{
    public override void Up()
    {
        Alter.Table("SalesOrderLines").InSchema("outbound")
            .AddColumn("PickedQuantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0);

        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "ADD CONSTRAINT CK_SalesOrderLines_PickedQty_NonNegative " +
            "CHECK (PickedQuantity >= 0);");

        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "ADD CONSTRAINT CK_SalesOrderLines_PickedQty_NotOverOrdered " +
            "CHECK (PickedQuantity <= OrderedQuantity);");
    }

    public override void Down()
    {
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "DROP CONSTRAINT CK_SalesOrderLines_PickedQty_NotOverOrdered;");
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "DROP CONSTRAINT CK_SalesOrderLines_PickedQty_NonNegative;");
        Delete.Column("PickedQuantity").FromTable("SalesOrderLines").InSchema("outbound");
    }
}
