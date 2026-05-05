using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504016L)]
[Tags("Tenant")]
public class Migration_20260504_016_CreateProductPackingConfigsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("ProductPackingConfigs").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // NO ACTION on Product delete: BLL must clean configs explicitly.
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_ProductPackingConfigs_Product",
                            "master", "Products", "Id")
                .OnDelete(System.Data.Rule.None)

            // NotNullable: pack mode is per (Product × UoM) — packing a case
            // and an each are distinct workflows even for the same SKU.
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_ProductPackingConfigs_Uom",
                            "master", "UnitsOfMeasure", "Id")
                .OnDelete(System.Data.Rule.None)

            // Nullable per spec: oversize / pallet-wrapped SKUs have no
            // suggested box. Pack station falls back to box-suggestion engine
            // when this is NULL.
            .WithColumn("SuggestedBoxTypeId").AsGuid().Nullable()
                .ForeignKey("FK_ProductPackingConfigs_BoxType",
                            "master", "BoxTypes", "Id")
                .OnDelete(System.Data.Rule.None)

            // Default 'ScanAndQty' — faster path for bulk items, the common
            // case. High-value / serialized SKUs override to 'ScanEach' at
            // setup time.
            .WithColumn("PackMode").AsAnsiString(20).NotNullable()
                .WithDefaultValue("ScanAndQty")

            // Soft-disable a config when packing strategy changes (e.g., new
            // box type rolled out) without losing the history row.
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Natural key: at most one config per (Product × UoM). Pack station
        // resolution must be deterministic — duplicate configs would make
        // "what mode for this scan?" ambiguous. Composite index also serves
        // ProductId-only lookups via leftmost-prefix.
        Create.Index("UX_ProductPackingConfigs_Product_Uom")
            .OnTable("ProductPackingConfigs").InSchema("master")
            .WithOptions().Unique()
            .OnColumn("ProductId").Ascending()
            .OnColumn("UomId").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[ProductPackingConfigs] " +
            "ADD CONSTRAINT CK_ProductPackingConfigs_PackMode " +
            "CHECK (PackMode IN ('ScanEach', 'ScanAndQty'));");
    }

    public override void Down() =>
        Delete.Table("ProductPackingConfigs").InSchema("master");
}
