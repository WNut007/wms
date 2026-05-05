using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// inbound.ReceivingLines — one row per (Product, lot, pallet) combo
// physically received within a ReceivingHeader. The orchestration
// service uses each line to drive (a) a stock upsert and (b) — if
// PurchaseOrderLineId is set — a PO line ReceivedQuantity bump.
//
// LotNumber / PalletNumber are stored as scanned at receive time;
// LotId / PalletId are populated by the orchestration *after* the
// β GetOrCreate calls resolve them, so each receiving line carries
// a hard FK pointer to the inventory rows it generated (audit trail).
//
// FK CreatedBy / UpdatedBy → security.Users wired in migration 059.
[Migration(20260504058L)]
[Tags("Tenant")]
public class Migration_20260504_058_CreateReceivingLinesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("ReceivingLines").InSchema("inbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // CASCADE: lines belong to header. Cancelling the header
            // cascades the lines too.
            .WithColumn("ReceivingHeaderId").AsGuid().NotNullable()
                .ForeignKey("FK_ReceivingLines_Header",
                            "inbound", "ReceivingHeaders", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("LineNumber").AsInt32().NotNullable()

            // Optional FK — pass NULL for blind receipts and "extras"
            // (more product than the PO ordered).
            .WithColumn("PurchaseOrderLineId").AsGuid().Nullable()
                .ForeignKey("FK_ReceivingLines_PoLine",
                            "inbound", "PurchaseOrderLines", "Id")

            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_ReceivingLines_Product", "master", "Products", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_ReceivingLines_Uom", "master", "UnitsOfMeasure", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_ReceivingLines_Owner", "master", "Owners", "Id")

            // Where the goods physically landed (typically a receiving
            // dock — putaway moves them later).
            .WithColumn("LocationId").AsGuid().NotNullable()
                .ForeignKey("FK_ReceivingLines_Location", "master", "Locations", "Id")

            .WithColumn("ReceivedQuantity").AsDecimal(18, 4).NotNullable()

            // Operator-entered identifiers — kept verbatim because
            // that's the audit-friendly form (matches what's on the
            // delivery paperwork). Resolved Ids below.
            .WithColumn("LotNumber").AsAnsiString(50).Nullable()
            .WithColumn("PalletNumber").AsAnsiString(50).Nullable()

            // Resolved by the orchestration after lot/pallet GetOrCreate.
            // Nullable because a no-lot, no-pallet receive leaves both
            // empty.
            .WithColumn("LotId").AsGuid().Nullable()
                .ForeignKey("FK_ReceivingLines_Lot", "inventory", "Lots", "Id")
            .WithColumn("PalletId").AsGuid().Nullable()
                .ForeignKey("FK_ReceivingLines_Pallet", "inventory", "Pallets", "Id")

            // Audit + optimistic concurrency.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable()
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Per-header LineNumber uniqueness — doubles as the ordering
        // index for displaying lines in their original sequence.
        Create.Index("UX_ReceivingLines_Header_LineNumber")
            .OnTable("ReceivingLines").InSchema("inbound")
            .WithOptions().Unique()
            .OnColumn("ReceivingHeaderId").Ascending()
            .OnColumn("LineNumber").Ascending();

        // PO line back-reference — "what receipts contributed to this
        // PO line?" feeds reconciliation reports.
        Create.Index("IX_ReceivingLines_PoLine")
            .OnTable("ReceivingLines").InSchema("inbound")
            .OnColumn("PurchaseOrderLineId").Ascending();

        // Per-product receipt history.
        Create.Index("IX_ReceivingLines_Product")
            .OnTable("ReceivingLines").InSchema("inbound")
            .OnColumn("ProductId").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [inbound].[ReceivingLines] " +
            "ADD CONSTRAINT CK_ReceivingLines_ReceivedQty_Positive " +
            "CHECK (ReceivedQuantity > 0);");
    }

    public override void Down() =>
        Delete.Table("ReceivingLines").InSchema("inbound");
}
