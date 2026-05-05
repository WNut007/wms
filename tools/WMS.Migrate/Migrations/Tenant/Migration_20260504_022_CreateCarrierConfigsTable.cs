using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504022L)]
[Tags("Tenant")]
public class Migration_20260504_022_CreateCarrierConfigsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("CarrierConfigs").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // CASCADE: deleting a Carrier removes its API/health config; soft
            // delete via Carriers.IsActive is the normal disable path.
            .WithColumn("CarrierId").AsGuid().NotNullable()
                .ForeignKey("FK_CarrierConfigs_Carriers", "master", "Carriers", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("ApiEndpoint").AsAnsiString(500).Nullable()
            // Encrypted at rest — decryption handled in BLL via tenant-scoped
            // key. VARBINARY(MAX) keeps the cipher payload opaque to DB tools.
            .WithColumn("ApiKeyEncrypted").AsCustom("VARBINARY(MAX)").Nullable()
            .WithColumn("ApiSecretEncrypted").AsCustom("VARBINARY(MAX)").Nullable()
            .WithColumn("WebhookUrl").AsAnsiString(500).Nullable()
            .WithColumn("WebhookSecret").AsCustom("VARBINARY(MAX)").Nullable()

            // Eager: call carrier API at order-create. Deferred: queue and
            // batch-call during slow periods. Default Eager — most carriers
            // need real-time AWB allocation.
            .WithColumn("Mode").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Eager")

            // Sandbox/Production split: a carrier can keep both rows so QA
            // and prod don't share credentials. Unique (CarrierId,Environment)
            // guards against duplicate configs in the same env.
            .WithColumn("Environment").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Sandbox")

            // Health snapshot updated by the periodic health-check job.
            // Nullable for fresh configs that haven't been probed yet.
            .WithColumn("HealthStatus").AsAnsiString(20).Nullable()
            .WithColumn("LastHealthCheck").AsDateTime2().Nullable()
            .WithColumn("Notes").AsCustom("NVARCHAR(MAX)").Nullable()

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // One config per (Carrier, Environment) — supports running Sandbox
        // alongside Production for the same carrier.
        Create.Index("UX_CarrierConfigs_Carrier_Environment")
            .OnTable("CarrierConfigs").InSchema("master")
            .WithOptions().Unique()
            .OnColumn("CarrierId").Ascending()
            .OnColumn("Environment").Ascending();

        // Filtered index — health-check job scans only configs that have been
        // probed at least once. FluentMigrator has no fluent filtered-index
        // API; raw SQL is the standard escape hatch.
        Execute.Sql(
            "CREATE INDEX IX_CarrierConfigs_Health " +
            "ON [master].[CarrierConfigs] (HealthStatus, LastHealthCheck) " +
            "WHERE HealthStatus IS NOT NULL;");

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[CarrierConfigs] " +
            "ADD CONSTRAINT CK_CarrierConfigs_Mode " +
            "CHECK (Mode IN ('Eager', 'Deferred'));");

        Execute.Sql(
            "ALTER TABLE [master].[CarrierConfigs] " +
            "ADD CONSTRAINT CK_CarrierConfigs_Environment " +
            "CHECK (Environment IN ('Sandbox', 'Production'));");

        // HealthStatus nullable — only enforced when set.
        Execute.Sql(
            "ALTER TABLE [master].[CarrierConfigs] " +
            "ADD CONSTRAINT CK_CarrierConfigs_HealthStatus " +
            "CHECK (HealthStatus IS NULL OR HealthStatus IN ('Healthy', 'Degraded', 'Down', 'Unknown'));");
    }

    public override void Down()
    {
        // Explicit DROP INDEX mirrors the explicit CREATE INDEX in Up; DROP TABLE
        // would also drop the index, but symmetry keeps the rollback obvious.
        Execute.Sql("DROP INDEX IX_CarrierConfigs_Health ON [master].[CarrierConfigs];");
        Delete.Table("CarrierConfigs").InSchema("master");
    }
}
