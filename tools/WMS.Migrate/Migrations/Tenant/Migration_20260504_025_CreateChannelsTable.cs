using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504025L)]
[Tags("Tenant")]
public class Migration_20260504_025_CreateChannelsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Channels").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Tenant-wide unique code (e.g. 'MANUAL', 'SHOPEE', 'LAZADA',
            // 'TIKTOK', 'B2B-PORTAL').
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(50).NotNullable()

            // CHECK enforces the four buckets — drives marketplace integration
            // routing, B2B pricing rules, and reporting facets.
            .WithColumn("Type").AsAnsiString(20).NotNullable()

            // Lower number = higher priority in channel selection UI.
            .WithColumn("DisplayOrder").AsInt32().Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Type-led picker for admin screens and reporting facets ("show all
        // active marketplaces").
        Create.Index("IX_Channels_Type").OnTable("Channels").InSchema("master")
            .OnColumn("Type").Ascending()
            .OnColumn("IsActive").Ascending()
            .OnColumn("DisplayOrder").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Channels] " +
            "ADD CONSTRAINT CK_Channels_Type " +
            "CHECK (Type IN ('Direct', 'Marketplace', 'B2B', 'Wholesale'));");
    }

    public override void Down() =>
        Delete.Table("Channels").InSchema("master");
}
