using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14D — outbound.PackTasks header. One pack task per SO.
//
// State flow (3-state, simpler than Pick's 5-state):
//   Pending → Packed | Cancelled
//
// Pending   = task generated; operator hasn't submitted yet
// Packed    = submitted; SO line PackedQty stamped + carton finalised
// Cancelled = pre-Submit reversal; SO returns to its pre-Generate state
//             (Picked or PartiallyPicked)
//
// No InProgress intermediate — pack workflow is single-shot (operator
// opens task, enters quantities + carton metadata, submits). If
// "Save Progress" lands in a future phase, Pending → InProgress →
// Packed can be added via a CHECK widening migration.
//
// Per-state audit trio (CLAUDE.md "Audit Field FK Rules" pattern,
// mirroring Phase 11A/12/13/14B/14C):
//   GeneratedAt/By  — always set on insert
//   PackedAt/By     — set when status flips Pending → Packed
//   CancelledAt/By + CancelReason — set when Pending → Cancelled
//
// CK_PackTasks_AuditMatchesStatus enforces the per-state invariant.
[Migration(20260510026L)]
[Tags("Tenant")]
public class Migration_20260510_026_CreatePackTasksTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("PackTasks").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // PACK-YYYYMMDD-NNNN. Tenant-wide unique.
            .WithColumn("PackNumber").AsAnsiString(50).NotNullable().Unique()

            .WithColumn("SalesOrderId").AsGuid().NotNullable()
                .ForeignKey("FK_PackTasks_SalesOrder",
                            "outbound", "SalesOrders", "Id")

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Pending")

            // Pool-mode for MVP; nullable for unassigned tasks.
            // Future: enforce required when per-station assignment lands.
            .WithColumn("AssignedTo").AsGuid().Nullable()
                .ForeignKey("FK_PackTasks_AssignedTo", "security", "Users", "Id")

            .WithColumn("Notes").AsString(1000).Nullable()

            // Per-state audit trio.
            .WithColumn("GeneratedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("GeneratedBy").AsGuid().Nullable()
                .ForeignKey("FK_PackTasks_GeneratedBy", "security", "Users", "Id")

            .WithColumn("PackedAt").AsDateTime2().Nullable()
            .WithColumn("PackedBy").AsGuid().Nullable()
                .ForeignKey("FK_PackTasks_PackedBy", "security", "Users", "Id")

            .WithColumn("CancelledAt").AsDateTime2().Nullable()
            .WithColumn("CancelledBy").AsGuid().Nullable()
                .ForeignKey("FK_PackTasks_CancelledBy", "security", "Users", "Id")
            .WithColumn("CancelReason").AsString(500).Nullable()

            // Standard audit + version.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PackTasks_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PackTasks_UpdatedBy", "security", "Users", "Id")
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Index("IX_PackTasks_Status")
            .OnTable("PackTasks").InSchema("outbound")
            .OnColumn("Status").Ascending()
            .OnColumn("GeneratedAt").Descending();

        Create.Index("IX_PackTasks_SalesOrder")
            .OnTable("PackTasks").InSchema("outbound")
            .OnColumn("SalesOrderId").Ascending();

        Create.Index("IX_PackTasks_AssignedTo")
            .OnTable("PackTasks").InSchema("outbound")
            .OnColumn("AssignedTo").Ascending()
            .OnColumn("Status").Ascending();

        Execute.Sql(
            "ALTER TABLE [outbound].[PackTasks] " +
            "ADD CONSTRAINT CK_PackTasks_Status " +
            "CHECK (Status IN ('Pending', 'Packed', 'Cancelled'));");

        // Per-state audit invariant. Mirrors Phase 11A/12/13/14B/14C
        // *_AuditMatchesStatus pattern.
        Execute.Sql(@"
ALTER TABLE [outbound].[PackTasks]
ADD CONSTRAINT CK_PackTasks_AuditMatchesStatus
CHECK (
    (Status = 'Pending'   AND PackedAt IS NULL     AND CancelledAt IS NULL)
 OR (Status = 'Packed'    AND PackedAt IS NOT NULL AND CancelledAt IS NULL)
 OR (Status = 'Cancelled' AND CancelledAt IS NOT NULL AND PackedAt IS NULL)
);");
    }

    public override void Down()
    {
        Delete.Table("PackTasks").InSchema("outbound");
    }
}
