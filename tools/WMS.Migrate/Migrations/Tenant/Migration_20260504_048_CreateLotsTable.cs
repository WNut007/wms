using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// inventory.Lots — per-product lot/batch identifiers driving FEFO
// rotation, expiry quarantine, and supplier-lot traceability.
//
// FK CreatedBy / UpdatedBy → security.Users is wired in migration 051
// alongside Pallets and Stock.
[Migration(20260504048L)]
[Tags("Tenant")]
public class Migration_20260504_048_CreateLotsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Lots").InSchema("inventory")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // FK → master.Products. NO ACTION on delete: a product with
            // lots in the system mustn't disappear silently — soft-delete
            // via IsActive on Products instead.
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_Lots_Products", "master", "Products", "Id")

            // LotNumber is unique within product, not globally — different
            // products may legitimately reuse the same supplier lot string.
            .WithColumn("LotNumber").AsAnsiString(50).NotNullable()

            .WithColumn("ReceivedDate").AsDate().NotNullable()
            .WithColumn("ExpiryDate").AsDate().Nullable()
            .WithColumn("ManufactureDate").AsDate().Nullable()
            .WithColumn("SupplierLotNumber").AsAnsiString(100).Nullable()

            // Lifecycle gate: Active → Quarantine (QC hold) → Hold
            // (admin-block) → Expired (post-expiry release). CHECK below
            // enforces the value set.
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

        // Per-product LotNumber uniqueness — doubles as the natural-key
        // lookup ("does this product already have lot ABC123?").
        Create.Index("UX_Lots_Product_Number")
            .OnTable("Lots").InSchema("inventory")
            .WithOptions().Unique()
            .OnColumn("ProductId").Ascending()
            .OnColumn("LotNumber").Ascending();

        // FEFO rotation reads — earliest expiry first across all products.
        Create.Index("IX_Lots_Expiry")
            .OnTable("Lots").InSchema("inventory")
            .OnColumn("ExpiryDate").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [inventory].[Lots] " +
            "ADD CONSTRAINT CK_Lots_Status " +
            "CHECK (Status IN ('Active', 'Quarantine', 'Hold', 'Expired'));");
    }

    public override void Down() =>
        Delete.Table("Lots").InSchema("inventory");
}
