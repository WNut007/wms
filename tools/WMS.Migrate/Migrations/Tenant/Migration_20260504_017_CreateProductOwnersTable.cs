using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504017L)]
[Tags("Tenant")]
public class Migration_20260504_017_CreateProductOwnersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("ProductOwners").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // NO ACTION on either side: ProductOwners is a junction with
            // history; BLL must close mappings explicitly before either parent
            // can be removed.
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_ProductOwners_Product",
                            "master", "Products", "Id")
                .OnDelete(System.Data.Rule.None)
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_ProductOwners_Owner",
                            "master", "Owners", "Id")
                .OnDelete(System.Data.Rule.None)

            // Owner's own SKU / barcode codes — often differ from the tenant's
            // canonical Product.Code / ProductBarcodes.Barcode (esp. 3PL/VMI
            // where the Owner uses their internal numbering).
            .WithColumn("OwnerSku").AsAnsiString(50).Nullable()
            .WithColumn("OwnerBarcode").AsAnsiString(50).Nullable()

            // Pricing — all nullable. A mapping may exist before pricing is
            // negotiated (draft state). Currency is also nullable: meaningful
            // only when at least one price column is set; BLL validates.
            .WithColumn("CostBasis").AsDecimal(18, 4).Nullable()
            .WithColumn("SettlementPrice").AsDecimal(18, 4).Nullable()
            .WithColumn("Currency").AsAnsiString(3).Nullable()

            // Default false — preferred is opt-in; allocation falls back to
            // the only-active mapping when no preferred row exists.
            .WithColumn("IsPreferred").AsBoolean().NotNullable().WithDefaultValue(0)

            // Soft-delete flag, distinct from EffectiveFrom/To:
            //  - IsActive = admin toggle (disable now, retroactively)
            //  - EffectiveFrom/To = scheduled date range (future-dated mappings
            //    may be IsActive=1 but not yet in effect)
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Default = today (UTC). Most mappings start effective immediately
            // on creation; future-dated rows are typed in by hand.
            .WithColumn("EffectiveFrom").AsDate().NotNullable()
                .WithDefaultValue(RawSql.Insert("CAST(SYSUTCDATETIME() AS DATE)"))
            .WithColumn("EffectiveTo").AsDate().Nullable()

            // Replenishment metadata — only meaningful for Supplier owners;
            // Self/3PL leave NULL.
            .WithColumn("LeadTimeDays").AsInt32().Nullable()
            .WithColumn("MinOrderQty").AsDecimal(18, 4).Nullable()

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Per spec — "all owners for this product, active first".
        // Singular index name 'IX_ProductOwner' matches docs/02 line 319 verbatim.
        Create.Index("IX_ProductOwner")
            .OnTable("ProductOwners").InSchema("master")
            .OnColumn("ProductId").Ascending()
            .OnColumn("IsActive").Ascending();

        // Per spec — "all products from this owner". Singular per spec.
        Create.Index("IX_OwnerProduct")
            .OnTable("ProductOwners").InSchema("master")
            .OnColumn("OwnerId").Ascending()
            .OnColumn("ProductId").Ascending();

        // Filtered unique: at most one currently-effective mapping per
        // (Product, Owner) pair. History rows (EffectiveTo NOT NULL) and
        // disabled rows (IsActive=0) are excluded so they don't conflict.
        Execute.Sql(
            "CREATE UNIQUE INDEX [UX_ProductOwners_ActivePair] " +
            "ON [master].[ProductOwners] ([ProductId], [OwnerId]) " +
            "WHERE [IsActive] = 1 AND [EffectiveTo] IS NULL;");

        // Filtered unique: at most one preferred owner per product among
        // active mappings. Allocation engine reads this directly.
        Execute.Sql(
            "CREATE UNIQUE INDEX [UX_ProductOwners_PreferredPerProduct] " +
            "ON [master].[ProductOwners] ([ProductId]) " +
            "WHERE [IsPreferred] = 1 AND [IsActive] = 1;");

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        // EffectiveTo NULL = open-ended; otherwise must be strictly after EffectiveFrom.
        Execute.Sql(
            "ALTER TABLE [master].[ProductOwners] " +
            "ADD CONSTRAINT CK_ProductOwners_EffectiveRange " +
            "CHECK (EffectiveTo IS NULL OR EffectiveTo > EffectiveFrom);");
    }

    public override void Down() =>
        Delete.Table("ProductOwners").InSchema("master");
}
