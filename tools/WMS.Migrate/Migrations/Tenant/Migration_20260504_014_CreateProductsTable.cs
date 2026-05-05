using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504014L)]
[Tags("Tenant")]
public class Migration_20260504_014_CreateProductsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Products").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // SKU — wider than typical master code (50 vs 20) because real-world
            // SKUs include vendor prefixes, variant suffixes, etc.
            .WithColumn("Code").AsAnsiString(50).NotNullable().Unique()
            .WithColumn("Name").AsString(200).NotNullable()

            // CategoryId NotNullable — products must belong to a category for
            // tree browse, allocation strategies, and stratification to work.
            // NO ACTION on category delete: BLL must reassign before drop.
            .WithColumn("CategoryId").AsGuid().NotNullable()
                .ForeignKey("FK_Products_Category",
                            "master", "ProductCategories", "Id")
                .OnDelete(System.Data.Rule.None)

            // BaseUomId NotNullable — stock math (Stock.Qty, conversions,
            // reservations) requires a base unit. NO ACTION: UoMs are reused
            // across many products; never cascade-delete from a single UoM row.
            .WithColumn("BaseUomId").AsGuid().NotNullable()
                .ForeignKey("FK_Products_BaseUom",
                            "master", "UnitsOfMeasure", "Id")
                .OnDelete(System.Data.Rule.None)

            // Default 'None' — most products don't track lots; opt-in per SKU.
            .WithColumn("TrackingMethod").AsAnsiString(20).NotNullable()
                .WithDefaultValue("None")

            // Nullable: ABC analysis runs periodically; new SKUs have no
            // velocity until the first review. VARCHAR(2) leaves room for
            // A+/B+ schemes without a CHECK constraint forcing a fixed enum.
            .WithColumn("VelocityClass").AsAnsiString(2).Nullable()

            // Default false: catch-weight (weigh at pick) is a fresh-foods
            // exception, not the norm.
            .WithColumn("UseCatchWeight").AsBoolean().NotNullable().WithDefaultValue(0)

            // Default 'Active' — onboarding flow creates products usable
            // immediately. CHECK enforces the four-state lifecycle.
            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Active")

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Per spec: category browse + status filter (e.g. "active SKUs in
        // Electronics > Mobile"). Index name 'IX_Product_Category' (singular)
        // matches docs/02 line 511 verbatim.
        Create.Index("IX_Product_Category")
            .OnTable("Products").InSchema("master")
            .OnColumn("CategoryId").Ascending()
            .OnColumn("Status").Ascending();

        // Status-led picker queries ("show all active products"). Status first
        // so the predicate is sargable; Code second for ordering.
        Create.Index("IX_Products_Status")
            .OnTable("Products").InSchema("master")
            .OnColumn("Status").Ascending()
            .OnColumn("Code").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Products] " +
            "ADD CONSTRAINT CK_Products_TrackingMethod " +
            "CHECK (TrackingMethod IN ('None', 'Lot', 'LotAndSerial'));");

        Execute.Sql(
            "ALTER TABLE [master].[Products] " +
            "ADD CONSTRAINT CK_Products_Status " +
            "CHECK (Status IN ('Active', 'Inactive', 'Discontinued', 'Draft'));");
    }

    public override void Down() =>
        Delete.Table("Products").InSchema("master");
}
