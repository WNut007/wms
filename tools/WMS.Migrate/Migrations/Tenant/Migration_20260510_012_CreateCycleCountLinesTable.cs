using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 12 — cycle count line items, snapshotted at session-create
// time. Each line represents one Stock row (6-tuple key) the
// operator must physically count.
//
// Snapshot policy: only positive-OnHand stock rows at create time.
// Zero-OnHand rows are skipped (counting "is there 0 here" rarely
// adds value and bloats sessions). If the operator finds unexpected
// stock at a location not in the session, they record it via
// Phase 11A's manual Adjustment workflow.
//
// CountedQuantity is NULL until the operator records a count.
// Variance = CountedQuantity - ExpectedQuantity (computed at
// apply-time; not persisted). LineStatus tracks per-line state:
//   'Pending'  — not yet counted (CountedQuantity IS NULL)
//   'Counted'  — count entered (CountedQuantity IS NOT NULL)
//   'Skipped'  — operator marked as "couldn't find / not present";
//                ignored on apply (Phase 12+ — for v1 operators
//                just leave Pending which apply-time treats the same)
//
// 6-tuple denormalised so a snapshot survives even if the underlying
// Stock row gets adjusted to zero between Counting and Applied.
[Migration(20260510012L)]
[Tags("Tenant")]
public class Migration_20260510_012_CreateCycleCountLinesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("CycleCountLines").InSchema("counts")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            .WithColumn("CycleCountId").AsGuid().NotNullable()
                .ForeignKey("FK_CycleCountLines_CycleCount",
                            "counts", "CycleCounts", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            // 1-based sequence within the session, useful for clipboard
            // print sheets (deferred to v2).
            .WithColumn("LineNumber").AsInt32().NotNullable()

            // Snapshot of the Stock row being counted. FK ensures the
            // row exists at create-time; if the operator manually
            // adjusts it to zero or away between snapshot and apply,
            // Apply handles it idempotently (Stock UpsertOnHand by
            // 6-tuple key, not by StockId).
            .WithColumn("StockId").AsGuid().NotNullable()
                .ForeignKey("FK_CycleCountLines_Stock",
                            "inventory", "Stock", "Id")

            // 6-tuple denormalised — survives Stock-row changes.
            .WithColumn("LocationId").AsGuid().NotNullable()
                .ForeignKey("FK_CycleCountLines_Location", "master", "Locations", "Id")
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_CycleCountLines_Product", "master", "Products", "Id")
            .WithColumn("LotId").AsGuid().Nullable()
                .ForeignKey("FK_CycleCountLines_Lot", "inventory", "Lots", "Id")
            .WithColumn("PalletId").AsGuid().Nullable()
                .ForeignKey("FK_CycleCountLines_Pallet", "inventory", "Pallets", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_CycleCountLines_Owner", "master", "Owners", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_CycleCountLines_Uom", "master", "UnitsOfMeasure", "Id")

            .WithColumn("ExpectedQuantity").AsDecimal(18, 4).NotNullable()
            .WithColumn("CountedQuantity").AsDecimal(18, 4).Nullable()

            .WithColumn("LineStatus").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Pending")

            .WithColumn("Notes").AsString(500).Nullable()

            // Standard audit (no Version on lines — operator may
            // overwrite freely until session is Counted/Applied).
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // "All lines for this session, in entry order" — the dominant
        // read.
        Create.Index("IX_CycleCountLines_CycleCount")
            .OnTable("CycleCountLines").InSchema("counts")
            .OnColumn("CycleCountId").Ascending()
            .OnColumn("LineNumber").Ascending();

        // Future "all sessions that touched this stock row".
        Create.Index("IX_CycleCountLines_Stock")
            .OnTable("CycleCountLines").InSchema("counts")
            .OnColumn("StockId").Ascending();

        Execute.Sql(@"
ALTER TABLE counts.CycleCountLines
ADD CONSTRAINT CK_CycleCountLines_LineStatus
CHECK (LineStatus IN ('Pending', 'Counted', 'Skipped'));");

        Execute.Sql(@"
ALTER TABLE counts.CycleCountLines
ADD CONSTRAINT CK_CycleCountLines_ExpectedQty_NonNegative
CHECK (ExpectedQuantity >= 0);");

        Execute.Sql(@"
ALTER TABLE counts.CycleCountLines
ADD CONSTRAINT CK_CycleCountLines_CountedQty_NonNegative
CHECK (CountedQuantity IS NULL OR CountedQuantity >= 0);");

        // 'Counted' status requires CountedQuantity populated.
        Execute.Sql(@"
ALTER TABLE counts.CycleCountLines
ADD CONSTRAINT CK_CycleCountLines_CountedRequiresQty
CHECK (
    (LineStatus = 'Counted'  AND CountedQuantity IS NOT NULL)
 OR (LineStatus = 'Pending'  AND CountedQuantity IS NULL)
 OR (LineStatus = 'Skipped')
);");
    }

    public override void Down() =>
        Delete.Table("CycleCountLines").InSchema("counts");
}
