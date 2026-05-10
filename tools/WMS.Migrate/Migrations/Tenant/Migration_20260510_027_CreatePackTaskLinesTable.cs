using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14D — outbound.PackTaskLines. One per SO line that had
// PickedQuantity > 0 at task generation. Lines that were skipped /
// short-picked to zero do NOT get a PackTaskLine (nothing to pack).
//
// Quantity progression:
//   PickedQuantity = SO line.PickedQuantity at task generation (read-only snapshot)
//   PackedQuantity = NULL until submit; then 0..PickedQuantity
//
// Per-line status:
//   Pending  — created, awaiting pack
//   Packed   — submitted with PackedQuantity populated (any value 0..Picked)
//   Skipped  — operator marked unpackable (e.g., damaged in transit
//              between pick and pack); ShortPackReason required
//
// CK_PackTaskLines_StatusMatchesQty enforces the (status,qty) invariant.
// CK_PackTaskLines_PackedNotOverPicked caps PackedQty at the snapshotted
// PickedQuantity ceiling (operator can never pack more than was picked).
//
// No Stock writes on submit — pack is post-stock; the qty already left
// inventory at pick submit (Phase 14C SubmitAsync). PackedQty < PickedQty
// surfaces as a discrepancy that needs follow-up (future return-to-stock
// adjustment) but the SO still flips to Packed since the carton is sealed.
//
// Schema mirrors Phase 14C PickTaskLines (snapshot pattern, no Version
// on lines per CycleCountLines + TransferOrderLines + PickTaskLines
// convention). Owner / Product / UoM snapshotted for stable display.
[Migration(20260510027L)]
[Tags("Tenant")]
public class Migration_20260510_027_CreatePackTaskLinesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("PackTaskLines").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // CASCADE: lines belong to the task. Cancelled tasks could
            // be hard-deleted via this path.
            .WithColumn("PackTaskId").AsGuid().NotNullable()
                .ForeignKey("FK_PackTaskLines_PackTask",
                            "outbound", "PackTasks", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("LineNumber").AsInt32().NotNullable()

            // FK to the SO line. NO ACTION on delete preserves history
            // when the SO line gets retired (rare; SO lines live forever
            // once allocated/picked).
            .WithColumn("SalesOrderLineId").AsGuid().NotNullable()
                .ForeignKey("FK_PackTaskLines_SalesOrderLine",
                            "outbound", "SalesOrderLines", "Id")

            // Snapshot fields (denormalized at task generation for
            // stable display + reporting).
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_PackTaskLines_Product", "master", "Products", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_PackTaskLines_Owner", "master", "Owners", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_PackTaskLines_Uom", "master", "UnitsOfMeasure", "Id")

            // Quantity progression.
            .WithColumn("PickedQuantity").AsDecimal(18, 4).NotNullable()  // = SO line.PickedQuantity at gen
            .WithColumn("PackedQuantity").AsDecimal(18, 4).Nullable()      // populated on submit; null = not packed yet

            // Per-line status: 'Pending' before submit, 'Packed' or
            // 'Skipped' after. Skipped = operator marked the line as
            // unpackable (e.g., damaged in transit between pick + pack).
            .WithColumn("LineStatus").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Pending")

            // Required at service layer when PackedQty < PickedQty
            // (short-pack at the carton — operator notes why) or when
            // LineStatus = 'Skipped'.
            .WithColumn("ShortPackReason").AsString(500).Nullable()

            .WithColumn("Notes").AsString(500).Nullable()

            // Standard audit (no Version on lines — same convention as
            // CycleCountLines + TransferOrderLines + PickTaskLines).
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PackTaskLines_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PackTaskLines_UpdatedBy", "security", "Users", "Id");

        Create.Index("UX_PackTaskLines_Task_LineNumber")
            .OnTable("PackTaskLines").InSchema("outbound")
            .WithOptions().Unique()
            .OnColumn("PackTaskId").Ascending()
            .OnColumn("LineNumber").Ascending();

        Create.Index("IX_PackTaskLines_SalesOrderLine")
            .OnTable("PackTaskLines").InSchema("outbound")
            .OnColumn("SalesOrderLineId").Ascending();

        Execute.Sql(
            "ALTER TABLE [outbound].[PackTaskLines] " +
            "ADD CONSTRAINT CK_PackTaskLines_LineStatus " +
            "CHECK (LineStatus IN ('Pending', 'Packed', 'Skipped'));");

        // Quantity invariants.
        Execute.Sql(
            "ALTER TABLE [outbound].[PackTaskLines] " +
            "ADD CONSTRAINT CK_PackTaskLines_Picked_Positive " +
            "CHECK (PickedQuantity > 0);");

        Execute.Sql(
            "ALTER TABLE [outbound].[PackTaskLines] " +
            "ADD CONSTRAINT CK_PackTaskLines_Packed_NonNegative " +
            "CHECK (PackedQuantity IS NULL OR PackedQuantity >= 0);");

        // Operator can't claim more packed than picked. Equal or less.
        Execute.Sql(
            "ALTER TABLE [outbound].[PackTaskLines] " +
            "ADD CONSTRAINT CK_PackTaskLines_PackedNotOverPicked " +
            "CHECK (PackedQuantity IS NULL OR PackedQuantity <= PickedQuantity);");

        // Per-line status invariant: populated qty must match status.
        Execute.Sql(@"
ALTER TABLE [outbound].[PackTaskLines]
ADD CONSTRAINT CK_PackTaskLines_StatusMatchesQty
CHECK (
    (LineStatus = 'Pending' AND PackedQuantity IS NULL)
 OR (LineStatus = 'Packed'  AND PackedQuantity IS NOT NULL)
 OR (LineStatus = 'Skipped' AND PackedQuantity IS NULL)
);");
    }

    public override void Down()
    {
        Delete.Table("PackTaskLines").InSchema("outbound");
    }
}
