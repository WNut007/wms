using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504018L)]
[Tags("Tenant")]
public class Migration_20260504_018_CreateCustomersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Customers").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(200).NotNullable()

            // CHECK enforces B2B/B2C — drives ordering UX, pricing model, and
            // the B2B-specific fields below (CompanyName/TaxId).
            .WithColumn("CustomerType").AsAnsiString(20).NotNullable()

            // B2B-only fields. NULL for B2C customers. DB does not enforce
            // "B2B requires CompanyName/TaxId" — BLL validates at write time
            // (avoids complex multi-column CHECK that fights bulk inserts).
            .WithColumn("CompanyName").AsString(200).Nullable()
            .WithColumn("TaxId").AsAnsiString(20).Nullable()

            .WithColumn("Email").AsAnsiString(100).Nullable()
            .WithColumn("Phone").AsAnsiString(20).Nullable()

            // TrueCommerce stratification — populated by periodic analysis.
            // CHECK allows NULL (new customers haven't been tiered yet).
            .WithColumn("CustomerTier").AsAnsiString(10).Nullable()
            .WithColumn("AnnualRevenue").AsDecimal(18, 2).Nullable()
            .WithColumn("OrdersPerMonth").AsInt32().Nullable()
            .WithColumn("AvgOrderValue").AsDecimal(18, 2).Nullable()

            // Default false — most customers aren't key/strategic; opt-in
            // flags set by sales/admin.
            .WithColumn("IsKeyAccount").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("IsStrategic").AsBoolean().NotNullable().WithDefaultValue(0)

            // Lower number = higher priority. Nullable so untiered customers
            // fall through to default allocation rules.
            .WithColumn("AllocationPriority").AsInt32().Nullable()
            .WithColumn("SafetyStockDays").AsInt32().Nullable()
            // 0.00-100.00 — DECIMAL(5,2) supports up to 999.99, headroom for
            // overshoot in reporting calculations.
            .WithColumn("PromisedFillRate").AsDecimal(5, 2).Nullable()

            // Logical FK to Carriers — Carriers table doesn't exist yet, so
            // no FK constraint here. A follow-up migration will add the FK
            // when Carriers lands.
            .WithColumn("PreferredCarrierId").AsGuid().Nullable()
            .WithColumn("DefaultPaymentTerms").AsAnsiString(50).Nullable()

            // Default 'Active' — most customers usable immediately. Suspended
            // distinct from Inactive: credit hold / temporary block vs. closed
            // account.
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

        // Per spec line 353 — tier-driven allocation lookup
        // ("which tier-1 customers next, ordered by priority").
        // Singular index name 'IX_Customer_Tier' matches spec verbatim.
        Create.Index("IX_Customer_Tier")
            .OnTable("Customers").InSchema("master")
            .OnColumn("CustomerTier").Ascending()
            .OnColumn("AllocationPriority").Ascending();

        // Status-led picker for admin/CSR screens ("show all active customers").
        Create.Index("IX_Customers_Status")
            .OnTable("Customers").InSchema("master")
            .OnColumn("Status").Ascending()
            .OnColumn("Code").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Customers] " +
            "ADD CONSTRAINT CK_Customers_Type " +
            "CHECK (CustomerType IN ('B2B', 'B2C'));");

        Execute.Sql(
            "ALTER TABLE [master].[Customers] " +
            "ADD CONSTRAINT CK_Customers_Status " +
            "CHECK (Status IN ('Active', 'Inactive', 'Suspended', 'Draft'));");

        // CustomerTier nullable — only enforced when set.
        Execute.Sql(
            "ALTER TABLE [master].[Customers] " +
            "ADD CONSTRAINT CK_Customers_Tier " +
            "CHECK (CustomerTier IS NULL OR CustomerTier IN ('Tier1', 'Tier2', 'Tier3', 'Tier4'));");
    }

    public override void Down() =>
        Delete.Table("Customers").InSchema("master");
}
