using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504007L)]
[Tags("Tenant")]
public class Migration_20260504_007_CreateLocationsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Locations").InSchema("master")
            // Identity
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Denormalized WarehouseId enables a per-warehouse unique scanner code.
            // App layer keeps this in sync with Zones.WarehouseId; FK below
            // enforces referential integrity to the warehouse itself.
            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_Locations_Warehouses", "master", "Warehouses", "Id")
            .WithColumn("ZoneId").AsGuid().NotNullable()
                // No cascade — locations hold inventory references; admin must
                // explicitly clean them up before a zone can be removed.
                .ForeignKey("FK_Locations_Zones", "master", "Zones", "Id")
            .WithColumn("Code").AsAnsiString(20).NotNullable()
            .WithColumn("Description").AsString(200).Nullable()

            // Physical dimensions (cm)
            .WithColumn("LengthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("WidthCm").AsDecimal(10, 2).Nullable()
            .WithColumn("HeightCm").AsDecimal(10, 2).Nullable()

            // Capacity (BC pattern)
            .WithColumn("CapacityVolumeCubicCm").AsDecimal(18, 2).Nullable()
            .WithColumn("CapacityWeightKg").AsDecimal(18, 2).Nullable()
            .WithColumn("CapacityPolicy").AsAnsiString(20).NotNullable().WithDefaultValue("NoLimit")

            // Rank (BC pattern) — lower BinRank is picked/filled first.
            .WithColumn("BinRank").AsInt32().NotNullable().WithDefaultValue(100)

            // Bin behavior
            .WithColumn("AllowMultipleItems").AsBoolean().NotNullable().WithDefaultValue(1)

            // Pick thresholds
            .WithColumn("MinPickQty").AsDecimal(18, 4).Nullable()
            .WithColumn("MaxPickQty").AsDecimal(18, 4).Nullable()

            // 3D coordinates (ADR-011: schema in Phase 1, viz in Phase 4).
            // Meters from warehouse origin; nullable so locations can exist
            // before their physical position has been measured/imported.
            .WithColumn("PositionX").AsDecimal(10, 3).Nullable()
            .WithColumn("PositionY").AsDecimal(10, 3).Nullable()
            .WithColumn("PositionZ").AsDecimal(10, 3).Nullable()
            .WithColumn("Rotation").AsDecimal(5, 2).NotNullable().WithDefaultValue(0)

            // Grouping for visualization + reporting
            .WithColumn("Aisle").AsAnsiString(10).Nullable()
            .WithColumn("Bay").AsInt32().Nullable()
            .WithColumn("Level").AsInt32().Nullable()

            // Display metadata
            .WithColumn("Show3D").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("DisplayColor").AsAnsiString(20).Nullable()
            .WithColumn("IsPickface").AsBoolean().NotNullable().WithDefaultValue(0)

            // Operational status
            .WithColumn("Status").AsAnsiString(20).NotNullable().WithDefaultValue("Active")
            .WithColumn("LastVerifiedAt").AsDateTime2().Nullable()

            // Audit
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Per-warehouse unique scanner code: 'A-12-3' resolves to exactly one
        // physical location within a warehouse, regardless of zone assignment.
        Create.Index("UX_Locations_Warehouse_Code")
            .OnTable("Locations").InSchema("master")
            .WithOptions().Unique()
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("Code").Ascending();

        // Primary lookup path (per schema spec): list locations within a zone
        // ordered by code for admin grids and putaway candidate retrieval.
        Create.Index("IX_Location_Zone")
            .OnTable("Locations").InSchema("master")
            .OnColumn("ZoneId").Ascending()
            .OnColumn("Code").Ascending();

        // Putaway/picking scoring relies on rank ordering across all locations.
        Create.Index("IX_Location_Rank")
            .OnTable("Locations").InSchema("master")
            .OnColumn("BinRank").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Locations] " +
            "ADD CONSTRAINT CK_Locations_CapacityPolicy " +
            "CHECK (CapacityPolicy IN ('NoLimit', 'ByVolume', 'ByWeight', 'ByEither', 'ByBoth'));");

        Execute.Sql(
            "ALTER TABLE [master].[Locations] " +
            "ADD CONSTRAINT CK_Locations_Status " +
            "CHECK (Status IN ('Active', 'Blocked', 'Maintenance'));");
    }

    public override void Down() =>
        Delete.Table("Locations").InSchema("master");
}
