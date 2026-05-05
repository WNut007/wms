using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504023L)]
[Tags("Tenant")]
public class Migration_20260504_023_CreateCarrierCoverageTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("CarrierCoverage").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // CASCADE: coverage rules are meaningless without their carrier.
            .WithColumn("CarrierId").AsGuid().NotNullable()
                .ForeignKey("FK_CarrierCoverage_Carriers", "master", "Carriers", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            // NULL hierarchy: NULL Province = applies to all provinces;
            // NULL District = all districts within the province; NULL
            // PostalCode = all postal codes within the district. The most
            // specific row wins at lookup time (BLL responsibility).
            .WithColumn("Province").AsString(50).Nullable()
            .WithColumn("District").AsString(50).Nullable()
            .WithColumn("PostalCode").AsAnsiString(10).Nullable()

            // Optional service-level scope — a carrier may only offer 'Same'
            // in Bangkok but 'Standard' province-wide. NULL = all levels.
            .WithColumn("ServiceLevel").AsAnsiString(20).Nullable()
            // Estimated business days — surfaced to the customer at checkout.
            .WithColumn("DeliveryDays").AsInt32().Nullable()

            // Remote-area surcharge applies when true. SurchargePerKg is the
            // per-kilogram premium; NULL means no surcharge configured.
            .WithColumn("IsRemoteArea").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("SurchargePerKg").AsDecimal(10, 2).Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Primary lookup path — checkout asks "does this carrier reach this
        // province/district?". Hits before postal code is known.
        Create.Index("IX_Coverage_Lookup")
            .OnTable("CarrierCoverage").InSchema("master")
            .OnColumn("CarrierId").Ascending()
            .OnColumn("Province").Ascending()
            .OnColumn("District").Ascending()
            .OnColumn("IsActive").Ascending();

        // Filtered index for postal-code precision lookup. FluentMigrator has
        // no fluent filtered-index API; raw SQL is the standard escape hatch.
        Execute.Sql(
            "CREATE INDEX IX_Coverage_Postal " +
            "ON [master].[CarrierCoverage] (PostalCode, IsActive) " +
            "WHERE PostalCode IS NOT NULL;");

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        // ServiceLevel nullable — only enforced when set.
        Execute.Sql(
            "ALTER TABLE [master].[CarrierCoverage] " +
            "ADD CONSTRAINT CK_CarrierCoverage_ServiceLevel " +
            "CHECK (ServiceLevel IS NULL OR ServiceLevel IN ('Same', 'Next', 'Standard', 'Economy'));");
    }

    public override void Down()
    {
        // Explicit DROP INDEX mirrors the explicit CREATE INDEX in Up; DROP TABLE
        // would also drop the index, but symmetry keeps the rollback obvious.
        Execute.Sql("DROP INDEX IX_Coverage_Postal ON [master].[CarrierCoverage];");
        Delete.Table("CarrierCoverage").InSchema("master");
    }
}
