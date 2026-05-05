using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504019L)]
[Tags("Tenant")]
public class Migration_20260504_019_CreateUomConversionsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("UomConversions").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Per-product conversion: pack ratios vary by SKU (Product X may
            // be 12 EA/CASE while Product Y is 6 EA/CASE).
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_UomConversions_Product",
                            "master", "Products", "Id")
                .OnDelete(System.Data.Rule.None)

            // Two FKs to the same parent (UnitsOfMeasure). NO ACTION on both
            // sides avoids SQL Server's multiple-cascade-path restriction.
            .WithColumn("FromUomId").AsGuid().NotNullable()
                .ForeignKey("FK_UomConversions_FromUom",
                            "master", "UnitsOfMeasure", "Id")
                .OnDelete(System.Data.Rule.None)
            .WithColumn("ToUomId").AsGuid().NotNullable()
                .ForeignKey("FK_UomConversions_ToUom",
                            "master", "UnitsOfMeasure", "Id")
                .OnDelete(System.Data.Rule.None)

            // Semantic: FromQty * Factor = ToQty
            // (e.g. From=CASE, To=EA, Factor=12  →  1 CASE * 12 = 12 EA).
            // CHECK Factor > 0 below prevents divide-by-zero / negative math.
            .WithColumn("Factor").AsDecimal(18, 6).NotNullable()

            // Dimensions describe the FromUom-level packaging — used by the
            // box-suggestion algorithm and carrier dim-weight billing. All
            // nullable: not every Product/UoM pair has measured dimensions.
            .WithColumn("LengthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("WidthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("HeightCm").AsDecimal(10, 2).Nullable()
            .WithColumn("VolumeCubicCm").AsDecimal(18, 2).Nullable()
            // 3-decimal precision: small items weigh ~0.005 kg and dim-weight
            // rounding can shift carrier billing brackets.
            .WithColumn("WeightKg").AsDecimal(10, 3).Nullable()

            // Soft-deprecate when a supplier changes pack size — preserves
            // history rows for retrospective stock conversion math.
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Per spec line 489 — non-unique lookup (covers active + history rows
        // for retrospective queries). Singular index name matches spec verbatim.
        Create.Index("IX_UomConversion")
            .OnTable("UomConversions").InSchema("master")
            .OnColumn("ProductId").Ascending()
            .OnColumn("FromUomId").Ascending()
            .OnColumn("ToUomId").Ascending();

        // Filtered unique: at most one currently-active conversion per
        // (Product, From, To). History rows (IsActive=0) excluded so
        // deprecation does not require hard-delete.
        Execute.Sql(
            "CREATE UNIQUE INDEX [UX_UomConversions_Triple] " +
            "ON [master].[UomConversions] ([ProductId], [FromUomId], [ToUomId]) " +
            "WHERE [IsActive] = 1;");

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[UomConversions] " +
            "ADD CONSTRAINT CK_UomConversions_Factor " +
            "CHECK (Factor > 0);");

        // Identity rows (Factor=1, From=To) waste a unique slot and have no
        // arithmetic value.
        Execute.Sql(
            "ALTER TABLE [master].[UomConversions] " +
            "ADD CONSTRAINT CK_UomConversions_DifferentUoms " +
            "CHECK (FromUomId <> ToUomId);");
    }

    public override void Down() =>
        Delete.Table("UomConversions").InSchema("master");
}
