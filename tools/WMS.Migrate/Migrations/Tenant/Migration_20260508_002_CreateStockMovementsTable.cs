using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// inventory.StockMovements — append-only log of every mutation to
// inventory.Stock. Per ADR-014: each row points at one Stock row via
// StockId, carries a SIGNED QuantityDelta (positive = onto the row,
// negative = off the row), and lives forever. INSERT is composed
// inside the SAME transaction as the Stock UPDATE / MERGE so the two
// can never disagree.
//
// Schema choices vs the original docs/02 draft are documented in
// ADR-014 — the short version: rename Qty→QuantityDelta (signed),
// StockId NOT NULL (every movement points at exactly one Stock row),
// no TenantId column (DB-per-tenant per ADR-001), CHECK on the closed
// MovementType enum, FK column references match the actual PK column
// names in this codebase (.Id everywhere).
//
// No BaseEntity audit columns (UpdatedAt/Version) — movements are
// immutable; PerformedAt is the only timestamp and there is no UPDATE
// path. Mirrors the intentional absence on security.AuditLog
// (migration 039) and master.SystemAuditLog.
[Migration(20260508002L)]
[Tags("Tenant")]
public class Migration_20260508_002_CreateStockMovementsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("StockMovements").InSchema("inventory")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Every movement ties to exactly one Stock row. NOT NULL
            // so the per-Stock history index is dense and so the
            // reconciliation invariant — SUM(QuantityDelta) per
            // StockId equals the row's net change since its first
            // recorded movement — is always well-defined.
            .WithColumn("StockId").AsGuid().NotNullable()
                .ForeignKey("FK_StockMovements_Stock",
                            "inventory", "Stock", "Id")

            // Closed enum + DB-side CHECK (added below). VARCHAR(20)
            // sized for the longest current value ('Transfer') with
            // headroom. Adding a new type means a follow-up migration
            // — by design.
            .WithColumn("MovementType").AsAnsiString(20).NotNullable()

            // Both nullable. Receive: From=NULL, To=loc.
            // Pick: From=loc, To=NULL. Putaway/Transfer: both set.
            // Adjust (in-place): both NULL.
            .WithColumn("FromLocationId").AsGuid().Nullable()
                .ForeignKey("FK_StockMovements_FromLoc",
                            "master", "Locations", "Id")
            .WithColumn("ToLocationId").AsGuid().Nullable()
                .ForeignKey("FK_StockMovements_ToLoc",
                            "master", "Locations", "Id")

            // Signed delta. Receive / dest of putaway = positive;
            // pick / source of putaway = negative. The sign is the
            // caller's responsibility — the column doesn't infer.
            // DECIMAL(18,4) matches inventory.Stock.QuantityOnHand so
            // partial-pack scenarios round-trip without precision loss.
            .WithColumn("QuantityDelta").AsDecimal(18, 4).NotNullable()

            // Pinned at insert so movements stay interpretable even
            // if the Stock row's UoM/Owner changes later via
            // reclassification (rare, but possible).
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_StockMovements_Uom",
                            "master", "UnitsOfMeasure", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_StockMovements_Owner",
                            "master", "Owners", "Id")

            // Provenance back to the domain row that drove this
            // movement. Both nullable because Putaway today has no
            // header table (TD-004 — closes when ADR-004 lands and
            // a PutawayOperations table appears). Receive: Type=
            // 'ReceivingLine', Id=<line guid>. Adjust / Transfer
            // (post ADR-013 / ADR-012): same shape.
            .WithColumn("ReferenceType").AsAnsiString(30).Nullable()
            .WithColumn("ReferenceId").AsGuid().Nullable()

            // Free-form note. NVARCHAR for unicode (Thai user input
            // on adjustment narratives, etc.).
            .WithColumn("Notes").AsString(500).Nullable()

            // Audit. PerformedBy NULL allowed for system actions per
            // CLAUDE.md "Audit Field FK Rules" — mirrors security.
            // AuditLog (migration 039). NO ACTION on delete: a user
            // with movement history must be soft-deleted (IsActive=0).
            .WithColumn("PerformedBy").AsGuid().Nullable()
                .ForeignKey("FK_StockMovements_PerformedBy",
                            "security", "Users", "Id")
                .OnDelete(System.Data.Rule.None)
            .WithColumn("PerformedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE()
                // (DATETIME precision); spec requires SYSUTCDATETIME()
                // (DATETIME2 precision). Same pattern as Stock + AuditLog.
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"));

        // FluentMigrator has no fluent CHECK constraint; raw SQL is
        // the standard escape hatch (mirrors Stock's CK constraints +
        // PurchaseOrders' CK_Status).
        Execute.Sql(
            "ALTER TABLE [inventory].[StockMovements] " +
            "ADD CONSTRAINT CK_StockMovements_MovementType " +
            "CHECK (MovementType IN " +
            "('Receive','Putaway','Pick','Adjust','Transfer','Return','Cycle'));");

        // Per-Stock activity feed (the hot read — every Activity tab
        // hit for a Stock row, every reconciliation per row). INCLUDE
        // keeps the page from chasing the heap for the columns the
        // panel renders, dropping IO for the common case.
        Execute.Sql(
            "CREATE INDEX IX_StockMovements_Stock " +
            "ON [inventory].[StockMovements] (StockId, PerformedAt DESC) " +
            "INCLUDE (MovementType, QuantityDelta, FromLocationId, ToLocationId);");

        // Provenance lookup ("what movements came from receiving line
        // X?"). Filtered to skip Putaway's NULL ReferenceId rows
        // (TD-004) so the index stays dense for the meaningful case
        // — same partial-index pattern as IX_AuditLog_Entity.
        Execute.Sql(
            "CREATE INDEX IX_StockMovements_Reference " +
            "ON [inventory].[StockMovements] (ReferenceType, ReferenceId) " +
            "WHERE ReferenceId IS NOT NULL;");

        // Global activity feed ("everything in the last 24 h"). Good
        // for ops dashboards + the future "show all activity" report.
        Execute.Sql(
            "CREATE INDEX IX_StockMovements_PerformedAt " +
            "ON [inventory].[StockMovements] (PerformedAt DESC);");
    }

    public override void Down()
    {
        // Explicit DROP INDEX mirrors the explicit CREATE INDEX in
        // Up; DROP TABLE would cascade them but symmetry keeps the
        // rollback obvious. Same pattern as AuditLog rollback.
        Execute.Sql("DROP INDEX IX_StockMovements_PerformedAt ON [inventory].[StockMovements];");
        Execute.Sql("DROP INDEX IX_StockMovements_Reference ON [inventory].[StockMovements];");
        Execute.Sql("DROP INDEX IX_StockMovements_Stock ON [inventory].[StockMovements];");
        Delete.Table("StockMovements").InSchema("inventory");
    }
}
