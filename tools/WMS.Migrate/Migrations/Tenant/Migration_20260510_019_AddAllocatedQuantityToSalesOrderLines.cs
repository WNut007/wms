using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14B — denormalized aggregate of OrderAllocations rows for the
// line. Stays in sync with the linking table inside the same TX as
// AllocateAsync / CancelAsync. Lets the SO Detail Lines panel render
// "5 / 12 allocated" without a per-line correlated subquery.
//
// Constraint: 0 <= AllocatedQuantity <= OrderedQuantity. Prevents
// over-allocation at the schema level — the service layer's strategy
// resolver picks at most (OrderedQuantity - AllocatedQuantity) per
// line, but defense in depth.
[Migration(20260510019L)]
[Tags("Tenant")]
public class Migration_20260510_019_AddAllocatedQuantityToSalesOrderLines : MigrationBase
{
    public override void Up()
    {
        Alter.Table("SalesOrderLines").InSchema("outbound")
            .AddColumn("AllocatedQuantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0);

        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "ADD CONSTRAINT CK_SalesOrderLines_AllocatedQty_NonNegative " +
            "CHECK (AllocatedQuantity >= 0);");

        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "ADD CONSTRAINT CK_SalesOrderLines_AllocatedQty_NotOverOrdered " +
            "CHECK (AllocatedQuantity <= OrderedQuantity);");
    }

    public override void Down()
    {
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "DROP CONSTRAINT CK_SalesOrderLines_AllocatedQty_NotOverOrdered;");
        Execute.Sql(
            "ALTER TABLE [outbound].[SalesOrderLines] " +
            "DROP CONSTRAINT CK_SalesOrderLines_AllocatedQty_NonNegative;");
        Delete.Column("AllocatedQuantity").FromTable("SalesOrderLines").InSchema("outbound");
    }
}
