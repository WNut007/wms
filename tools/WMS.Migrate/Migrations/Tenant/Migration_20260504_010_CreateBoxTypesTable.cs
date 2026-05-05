using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504010L)]
[Tags("Tenant")]
public class Migration_20260504_010_CreateBoxTypesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("BoxTypes").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Tenant-wide unique code — no warehouse scoping.
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(50).NotNullable()

            // Internal dimensions — usable space for fit calculations
            // (box-suggestion algorithm uses these against item dims).
            .WithColumn("InternalLengthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("InternalWidthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("InternalHeightCm").AsDecimal(10, 2).Nullable()
            // Stored separately rather than computed: admin may override the
            // L*W*H product when usable interior < bounding box.
            .WithColumn("InternalVolumeCubicCm").AsDecimal(18, 2).Nullable()

            // External dimensions — bounding box for carrier dim-weight pricing.
            .WithColumn("ExternalLengthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("ExternalWidthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("ExternalHeightCm").AsDecimal(10, 2).Nullable()

            // 3-decimal precision: small boxes weigh ~0.150 kg and parcel
            // weight rounding can shift carrier billing brackets.
            .WithColumn("EmptyWeightKg").AsDecimal(10, 3).Nullable()
            .WithColumn("MaxLoadKg").AsDecimal(10, 2).Nullable()

            // Procurement unit cost — feeds 3PL packaging billing.
            .WithColumn("UnitCost").AsDecimal(10, 2).Nullable()

            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        // Active-types lookup for box picker dropdowns and the box-suggestion
        // algorithm at packing.
        Create.Index("IX_BoxTypes_Active").OnTable("BoxTypes").InSchema("master")
            .OnColumn("IsActive").Ascending()
            .OnColumn("Code").Ascending();
    }

    public override void Down() =>
        Delete.Table("BoxTypes").InSchema("master");
}
