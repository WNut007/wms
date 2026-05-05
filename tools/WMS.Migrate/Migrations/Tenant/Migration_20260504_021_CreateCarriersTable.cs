using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504021L)]
[Tags("Tenant")]
public class Migration_20260504_021_CreateCarriersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Carriers").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Tenant-wide unique code — same carrier rolled out to every warehouse
            // (e.g. 'FLASH', 'KERRY', 'JT', 'THAIPOST').
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(100).NotNullable()

            // Nullable: classification is advisory metadata for UI grouping; not
            // every carrier fits cleanly into one bucket. CHECK enforces values
            // when set.
            .WithColumn("Type").AsAnsiString(20).Nullable()
            .WithColumn("Country").AsAnsiString(2).Nullable().WithDefaultValue("TH")

            // Lifecycle gate for carrier rollout — admins move through Inactive →
            // Configured (creds entered) → Tested (sandbox passed) → Production.
            // Default 'Inactive' so a freshly-created carrier can't accidentally
            // route real shipments before testing.
            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Inactive")

            // JSON array of service-level codes the carrier supports
            // (e.g. ["Same","Next","Standard","Economy"]) — drives UI dropdowns
            // and rate-card filtering. Free-form payload, app-layer validates.
            .WithColumn("SupportedServiceLevels").AsCustom("NVARCHAR(MAX)").Nullable()
            .WithColumn("WebsiteUrl").AsAnsiString(500).Nullable()
            .WithColumn("LogoUrl").AsAnsiString(500).Nullable()
            // Lower number = higher priority in carrier selection UI.
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

        // Status-led picker for admin screens ("show carriers ready for prod").
        Create.Index("IX_Carriers_Status").OnTable("Carriers").InSchema("master")
            .OnColumn("Status").Ascending()
            .OnColumn("IsActive").Ascending()
            .OnColumn("Code").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Carriers] " +
            "ADD CONSTRAINT CK_Carriers_Status " +
            "CHECK (Status IN ('Inactive', 'Configured', 'Tested', 'Production'));");

        // Type nullable — only enforced when set.
        Execute.Sql(
            "ALTER TABLE [master].[Carriers] " +
            "ADD CONSTRAINT CK_Carriers_Type " +
            "CHECK (Type IS NULL OR Type IN ('Express', 'Standard', 'Postal', 'Bike'));");
    }

    public override void Down() =>
        Delete.Table("Carriers").InSchema("master");
}
