using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504009L)]
[Tags("Tenant")]
public class Migration_20260504_009_CreateContainerTypesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("ContainerTypes").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Tenant-wide unique code — no warehouse scoping (definitions are
            // shared across all warehouses within the tenant).
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(50).NotNullable()
            .WithColumn("Type").AsAnsiString(20).NotNullable()
            // Physical dimensions — nullable; conceptual types may omit them.
            .WithColumn("LengthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("WidthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("HeightCm").AsDecimal(10, 2).Nullable()
            // Stored separately rather than computed: admin may override the
            // L*W*H product (e.g., usable interior < bounding box).
            .WithColumn("VolumeCubicCm").AsDecimal(18, 2).Nullable()
            .WithColumn("MaxWeightKg").AsDecimal(10, 2).Nullable()
            // Default false — opt-in flag. Pallets/totes set this true; corrugated
            // boxes typically stay false.
            .WithColumn("IsReusable").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        // Active-types lookup for picker dropdowns and putaway scoring.
        Create.Index("IX_ContainerTypes_Active").OnTable("ContainerTypes").InSchema("master")
            .OnColumn("IsActive").Ascending()
            .OnColumn("Code").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[ContainerTypes] " +
            "ADD CONSTRAINT CK_ContainerTypes_Type " +
            "CHECK (Type IN ('Pallet', 'Box', 'Tote', 'Cage', 'Crate'));");
    }

    public override void Down() =>
        Delete.Table("ContainerTypes").InSchema("master");
}
