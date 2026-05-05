using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504024L)]
[Tags("Tenant")]
public class Migration_20260504_024_CreateCarrierRatesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("CarrierRates").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // CASCADE: rate cards are meaningless without their carrier.
            .WithColumn("CarrierId").AsGuid().NotNullable()
                .ForeignKey("FK_CarrierRates_Carriers", "master", "Carriers", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            // Coarse zoning — Bangkok / Central / North / South / Remote etc.
            // Free-form string rather than enum CHECK: zone definitions vary
            // per carrier contract; app-layer validates against the carrier's
            // supported zones.
            .WithColumn("Zone").AsAnsiString(20).NotNullable()
            // Optional service-level scope — same Zone may have different
            // rates for Same-day vs Standard. NULL = applies to all levels.
            .WithColumn("ServiceLevel").AsAnsiString(20).Nullable()

            // Weight bracket: rate applies for WeightFromKg < weight <= WeightToKg.
            // CK enforces strict ordering so a malformed bracket can't slip in.
            .WithColumn("WeightFromKg").AsDecimal(10, 2).NotNullable()
            .WithColumn("WeightToKg").AsDecimal(10, 2).NotNullable()
            .WithColumn("Price").AsDecimal(10, 2).NotNullable()

            // Time-bounded rate cards — billing engine picks the row whose
            // [EffectiveFrom, EffectiveTo] window contains the ship date.
            // EffectiveTo NULL means "open-ended / current" — superseded when
            // a newer row with the same Zone+Level becomes effective.
            .WithColumn("EffectiveFrom").AsDate().NotNullable()
            .WithColumn("EffectiveTo").AsDate().Nullable()

            .WithColumn("Currency").AsAnsiString(3).NotNullable()
                .WithDefaultValue("THB")
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Time-windowed primary lookup ("which rate is current for this ship
        // date?"). EffectiveFrom DESC so the most-recent-applicable row sorts
        // first within a Zone+Level group.
        Create.Index("IX_CarrierRates_Active")
            .OnTable("CarrierRates").InSchema("master")
            .OnColumn("CarrierId").Ascending()
            .OnColumn("EffectiveFrom").Descending()
            .OnColumn("EffectiveTo").Ascending()
            .OnColumn("Zone").Ascending()
            .OnColumn("ServiceLevel").Ascending();

        // Secondary lookup for admin/CSR rate-card screens grouped by Zone.
        Create.Index("IX_CarrierRates_Lookup")
            .OnTable("CarrierRates").InSchema("master")
            .OnColumn("CarrierId").Ascending()
            .OnColumn("Zone").Ascending()
            .OnColumn("ServiceLevel").Ascending()
            .OnColumn("IsActive").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[CarrierRates] " +
            "ADD CONSTRAINT CK_CarrierRates_Currency " +
            "CHECK (Currency IN ('THB', 'USD'));");

        // Strict ordering — WeightToKg must exceed WeightFromKg so a row's
        // bracket is non-empty.
        Execute.Sql(
            "ALTER TABLE [master].[CarrierRates] " +
            "ADD CONSTRAINT CK_CarrierRates_WeightRange " +
            "CHECK (WeightToKg > WeightFromKg);");

        // ServiceLevel nullable — only enforced when set.
        Execute.Sql(
            "ALTER TABLE [master].[CarrierRates] " +
            "ADD CONSTRAINT CK_CarrierRates_ServiceLevel " +
            "CHECK (ServiceLevel IS NULL OR ServiceLevel IN ('Same', 'Next', 'Standard', 'Economy'));");
    }

    public override void Down() =>
        Delete.Table("CarrierRates").InSchema("master");
}
