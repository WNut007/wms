using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// inventory.Stock — quantity at the (LocationId, ProductId, LotId,
// PalletId, OwnerId, UomId) 6-tuple. Lot and Pallet are nullable for
// products that don't track them.
//
// **Uniqueness on the 6-tuple is enforced at the service layer**, not
// in this index, because SQL Server composite UNIQUE doesn't have
// NULL-safe semantics — two rows with the same (Loc, Prod, NULL,
// NULL, Owner, Uom) wouldn't violate a plain UNIQUE. Callers
// (StockService) implement upsert via NULL-safe SELECT followed by
// INSERT or version-checked UPDATE. The composite IX_Stock_Key gives
// that lookup an index seek; concurrency is guarded by the inherited
// Version column.
//
// FK CreatedBy / UpdatedBy → security.Users is wired in migration 051.
[Migration(20260504050L)]
[Tags("Tenant")]
public class Migration_20260504_050_CreateStockTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Stock").InSchema("inventory")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // The 6-tuple natural key. NO ACTION on delete throughout —
            // we never want a Stock row whose Location / Product / Owner /
            // Uom suddenly stops existing.
            .WithColumn("LocationId").AsGuid().NotNullable()
                .ForeignKey("FK_Stock_Location", "master", "Locations", "Id")
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_Stock_Product", "master", "Products", "Id")
            .WithColumn("LotId").AsGuid().Nullable()
                .ForeignKey("FK_Stock_Lot", "inventory", "Lots", "Id")
            .WithColumn("PalletId").AsGuid().Nullable()
                .ForeignKey("FK_Stock_Pallet", "inventory", "Pallets", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_Stock_Owner", "master", "Owners", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_Stock_Uom", "master", "UnitsOfMeasure", "Id")

            // DECIMAL(18,4) covers up to 14 integer digits + 4 fractional —
            // fits split-pack scenarios without exotic types.
            .WithColumn("QuantityOnHand").AsDecimal(18, 4).NotNullable()
                .WithDefaultValue(0)
            .WithColumn("QuantityAllocated").AsDecimal(18, 4).NotNullable()
                .WithDefaultValue(0)

            // Stamped on every receive / putaway / pick / adjust so
            // dead-stock reports can lean on a single column.
            .WithColumn("LastMovementAt").AsDateTime2().Nullable()

            // Audit + optimistic-concurrency Version.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable()
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Composite NON-UNIQUE index on the 6-tuple — the service-layer
        // upsert path leans on it for a fast seek before deciding INSERT
        // vs version-checked UPDATE.
        Create.Index("IX_Stock_Key")
            .OnTable("Stock").InSchema("inventory")
            .OnColumn("LocationId").Ascending()
            .OnColumn("ProductId").Ascending()
            .OnColumn("LotId").Ascending()
            .OnColumn("PalletId").Ascending()
            .OnColumn("OwnerId").Ascending()
            .OnColumn("UomId").Ascending();

        // Reverse-direction lookups — "all stock for product X" is the
        // primary report driver; "all stock at location Y" is the bin
        // detail page.
        Create.Index("IX_Stock_Product")
            .OnTable("Stock").InSchema("inventory")
            .OnColumn("ProductId").Ascending();

        Create.Index("IX_Stock_Location")
            .OnTable("Stock").InSchema("inventory")
            .OnColumn("LocationId").Ascending();

        // Quantity invariants — a row with negative on-hand is a data
        // bug; allocated above on-hand is over-promising. CHECK below.
        Execute.Sql(
            "ALTER TABLE [inventory].[Stock] " +
            "ADD CONSTRAINT CK_Stock_OnHand_NonNegative " +
            "CHECK (QuantityOnHand >= 0);");

        Execute.Sql(
            "ALTER TABLE [inventory].[Stock] " +
            "ADD CONSTRAINT CK_Stock_Allocated_NonNegative " +
            "CHECK (QuantityAllocated >= 0);");

        Execute.Sql(
            "ALTER TABLE [inventory].[Stock] " +
            "ADD CONSTRAINT CK_Stock_Allocated_NotOverOnHand " +
            "CHECK (QuantityAllocated <= QuantityOnHand);");
    }

    public override void Down() =>
        Delete.Table("Stock").InSchema("inventory");
}
