using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 12 — cycle count session header.
//
// Workflow: Counting → Review → Applied; Cancelled is a terminal
// state reachable from any non-Applied state. Apply happens
// atomically as part of approval (no separate Approved state).
//
// LocationFilter is nullable: when null, the snapshot covered the
// entire warehouse (all positive-OnHand stock rows whose Location
// belongs to WarehouseId). When set, only that single Location's
// stock was snapshot. Future v2: ProductFilter / Range, ABC class,
// scheduled / recurring.
//
// CK_CycleCounts_AuditMatchesStatus enforces per-status invariant
// on the audit trio (CountedAt, ReviewedAt/By, AppliedAt,
// CancelledAt/By/Reason). Counter+Reviewer+Approver collapse into
// the same column space — Counter recorded via CountedBy on lines
// (Phase 12+); for v1 we record the session-level CountedBy on the
// header.
[Migration(20260510011L)]
[Tags("Tenant")]
public class Migration_20260510_011_CreateCycleCountsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("CycleCounts").InSchema("counts")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            .WithColumn("CountNumber").AsAnsiString(50).NotNullable().Unique()

            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_CycleCounts_Warehouse",
                            "master", "Warehouses", "Id")

            // NULL = whole-warehouse scope; set = single-Location scope.
            .WithColumn("LocationFilter").AsGuid().Nullable()
                .ForeignKey("FK_CycleCounts_LocationFilter",
                            "master", "Locations", "Id")

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Counting")

            .WithColumn("Notes").AsString(1000).Nullable()

            // Per-state audit trio. StartedBy / StartedAt = creator
            // (NOT NULL — every count has a starter).
            .WithColumn("StartedBy").AsGuid().NotNullable()
                .ForeignKey("FK_CycleCounts_StartedBy", "security", "Users", "Id")
            .WithColumn("StartedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))

            // CountedAt = when the operator hit "Submit for review".
            // CountedBy = same user (single-counter v1 — multi-counter
            // is Phase 12+ via per-line CountedBy on lines table).
            .WithColumn("CountedBy").AsGuid().Nullable()
                .ForeignKey("FK_CycleCounts_CountedBy", "security", "Users", "Id")
            .WithColumn("CountedAt").AsDateTime2().Nullable()

            // ReviewedBy / At = approver (separation-of-duties enforced
            // at service layer: counter ≠ reviewer).
            .WithColumn("ReviewedBy").AsGuid().Nullable()
                .ForeignKey("FK_CycleCounts_ReviewedBy", "security", "Users", "Id")
            .WithColumn("ReviewedAt").AsDateTime2().Nullable()
            .WithColumn("AppliedAt").AsDateTime2().Nullable()

            // CancelledBy / At / Reason — same shape as
            // inbound.ReceivingHeaders (Phase 10B precedent).
            .WithColumn("CancelledBy").AsGuid().Nullable()
                .ForeignKey("FK_CycleCounts_CancelledBy", "security", "Users", "Id")
            .WithColumn("CancelledAt").AsDateTime2().Nullable()
            .WithColumn("CancelReason").AsString(500).Nullable()

            // Standard audit + version.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable()
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Per-warehouse list filter + status queue.
        Create.Index("IX_CycleCounts_Warehouse")
            .OnTable("CycleCounts").InSchema("counts")
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("StartedAt").Descending();

        Create.Index("IX_CycleCounts_Status")
            .OnTable("CycleCounts").InSchema("counts")
            .OnColumn("Status").Ascending()
            .OnColumn("StartedAt").Descending();

        Execute.Sql(@"
ALTER TABLE counts.CycleCounts
ADD CONSTRAINT CK_CycleCounts_Status
CHECK (Status IN ('Counting', 'Review', 'Applied', 'Cancelled'));");

        Execute.Sql(@"
ALTER TABLE counts.CycleCounts
ADD CONSTRAINT CK_CycleCounts_AuditMatchesStatus
CHECK (
    (Status = 'Counting'  AND CountedAt IS NULL AND ReviewedAt IS NULL AND AppliedAt IS NULL AND CancelledAt IS NULL)
 OR (Status = 'Review'    AND CountedAt IS NOT NULL AND ReviewedAt IS NULL AND AppliedAt IS NULL AND CancelledAt IS NULL)
 OR (Status = 'Applied'   AND CountedAt IS NOT NULL AND ReviewedAt IS NOT NULL AND AppliedAt IS NOT NULL AND CancelledAt IS NULL)
 OR (Status = 'Cancelled' AND CancelledAt IS NOT NULL AND AppliedAt IS NULL)
);");
    }

    public override void Down() =>
        Delete.Table("CycleCounts").InSchema("counts");
}
