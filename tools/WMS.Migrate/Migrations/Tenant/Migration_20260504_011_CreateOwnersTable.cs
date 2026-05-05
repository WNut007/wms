using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504011L)]
[Tags("Tenant")]
public class Migration_20260504_011_CreateOwnersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Owners").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Tenant-wide unique code — Owners are shared across all warehouses
            // within the tenant (one Supplier may stock many warehouses).
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(200).NotNullable()
            // Distinct from Name: legal entity for invoicing/tax docs may differ
            // from the display name used in operational screens.
            .WithColumn("LegalName").AsString(200).Nullable()

            // NotNullable + CHECK: Owner classification drives billing model,
            // stock ownership semantics, and portal access — never blank.
            .WithColumn("OwnerType").AsAnsiString(20).NotNullable()

            .WithColumn("ContactName").AsString(100).Nullable()
            .WithColumn("Email").AsAnsiString(100).Nullable()
            .WithColumn("Phone").AsAnsiString(20).Nullable()
            .WithColumn("Address").AsString(500).Nullable()
            .WithColumn("TaxId").AsAnsiString(20).Nullable()

            // Default false — opt-in flag. Most Owners (esp. Self) have no portal
            // login; only VMI/Customer3PL typically get portal access.
            .WithColumn("HasPortalAccess").AsBoolean().NotNullable().WithDefaultValue(0)
            // Free-text strings rather than enum CHECKs: settlement options vary
            // per tenant contract (Daily/Weekly/Monthly, FIFO/Average/etc.) —
            // app-layer validation against tenant config is more flexible.
            .WithColumn("SettlementFrequency").AsAnsiString(20).Nullable()
            .WithColumn("SettlementPriceModel").AsAnsiString(20).Nullable()
            .WithColumn("PaymentTerms").AsAnsiString(50).Nullable()

            // Nullable: only 3PL/VMI Owners have storage/handling rates;
            // Self/Supplier typically leave these blank.
            .WithColumn("StorageRatePerPalletDay").AsDecimal(10, 2).Nullable()
            .WithColumn("HandlingRatePerOrder").AsDecimal(10, 2).Nullable()

            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        // Type-filtered active lookup: billing engine queries "all active 3PL
        // customers" and "all active suppliers" — composite index serves both.
        Create.Index("IX_Owners_Type").OnTable("Owners").InSchema("master")
            .OnColumn("OwnerType").Ascending()
            .OnColumn("IsActive").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Owners] " +
            "ADD CONSTRAINT CK_Owners_OwnerType " +
            "CHECK (OwnerType IN ('Self', 'Supplier', 'VMI', 'Customer3PL'));");
    }

    public override void Down() =>
        Delete.Table("Owners").InSchema("master");
}
