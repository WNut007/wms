using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504015L)]
[Tags("Tenant")]
public class Migration_20260504_015_CreateProductBarcodesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("ProductBarcodes").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // NO ACTION on Product delete: barcodes don't auto-cascade. Hard
            // delete of a product is rare (Status='Discontinued' is preferred);
            // BLL must explicitly remove barcodes.
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_ProductBarcodes_Product",
                            "master", "Products", "Id")
                .OnDelete(System.Data.Rule.None)

            // Globally unique across the tenant — standard EAN/UPC semantics.
            // The auto-created index name is IX_ProductBarcodes_Barcode.
            .WithColumn("Barcode").AsAnsiString(50).NotNullable().Unique()

            // NotNullable: each barcode binds to a specific UoM (case vs each
            // vs pack barcodes differ). NO ACTION — UoMs are shared.
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_ProductBarcodes_Uom",
                            "master", "UnitsOfMeasure", "Id")
                .OnDelete(System.Data.Rule.None)

            // Default false. Exactly one primary per product is enforced by
            // the filtered unique index UX_ProductBarcodes_PrimaryPerProduct
            // below — DB-level integrity, not just BLL convention.
            .WithColumn("IsPrimary").AsBoolean().NotNullable().WithDefaultValue(0)

            // Soft-delete flag — supplier barcode changes are common; the old
            // barcode often needs to remain scannable during transition.
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // "Find all barcodes for a product, primary first." DESC on IsPrimary
        // surfaces the canonical row at the top of the result set.
        Create.Index("IX_ProductBarcodes_Product")
            .OnTable("ProductBarcodes").InSchema("master")
            .OnColumn("ProductId").Ascending()
            .OnColumn("IsPrimary").Descending();

        // Filtered unique index — at most one primary barcode per product.
        // FluentMigrator has no fluent filtered-index syntax; raw SQL is the
        // standard escape hatch. Dropped automatically with the table on Down.
        Execute.Sql(
            "CREATE UNIQUE INDEX [UX_ProductBarcodes_PrimaryPerProduct] " +
            "ON [master].[ProductBarcodes] ([ProductId]) " +
            "WHERE [IsPrimary] = 1;");
    }

    public override void Down() =>
        Delete.Table("ProductBarcodes").InSchema("master");
}
