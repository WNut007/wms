using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14C — outbound.PickTasks + outbound.PickTaskLines. One pick
// task per SO; one line per OrderAllocation.
//
// State flow:
//   Pending → InProgress → Picked | PartiallyPicked | Cancelled
//
// Pending     = task generated; no quantities entered yet
// InProgress  = operator has entered at least one quantity (via "Save
//               progress")
// Picked      = submitted; every line picked at full expected qty
// PartiallyPicked = submitted; at least one line short on stock
// Cancelled   = task cancelled pre-commit (allocations back to Active)
//
// Per-state audit trio + CK_PickTasks_AuditMatchesStatus invariant
// per the established Phase 11A/12/13 pattern.
//
// Bundled in one migration (header + lines) for table-pair coherence
// — same pattern as Phase 13's TransferOrders/Lines.
[Migration(20260510024L)]
[Tags("Tenant")]
public class Migration_20260510_024_CreatePickTasksAndLines : MigrationBase
{
    public override void Up()
    {
        // ====================================================
        // PickTasks header
        // ====================================================
        Create.Table("PickTasks").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            .WithColumn("PickNumber").AsAnsiString(50).NotNullable().Unique()

            .WithColumn("SalesOrderId").AsGuid().NotNullable()
                .ForeignKey("FK_PickTasks_SalesOrder",
                            "outbound", "SalesOrders", "Id")

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Pending")

            // Pool-mode for MVP; nullable for unassigned tasks.
            // Future: enforce required when per-picker assignment lands.
            .WithColumn("AssignedTo").AsGuid().Nullable()
                .ForeignKey("FK_PickTasks_AssignedTo", "security", "Users", "Id")

            .WithColumn("Notes").AsString(1000).Nullable()

            // Per-state audit trio.
            .WithColumn("GeneratedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("GeneratedBy").AsGuid().Nullable()
                .ForeignKey("FK_PickTasks_GeneratedBy", "security", "Users", "Id")

            .WithColumn("StartedAt").AsDateTime2().Nullable()
            .WithColumn("StartedBy").AsGuid().Nullable()
                .ForeignKey("FK_PickTasks_StartedBy", "security", "Users", "Id")

            .WithColumn("CompletedAt").AsDateTime2().Nullable()
            .WithColumn("CompletedBy").AsGuid().Nullable()
                .ForeignKey("FK_PickTasks_CompletedBy", "security", "Users", "Id")

            .WithColumn("CancelledAt").AsDateTime2().Nullable()
            .WithColumn("CancelledBy").AsGuid().Nullable()
                .ForeignKey("FK_PickTasks_CancelledBy", "security", "Users", "Id")
            .WithColumn("CancelReason").AsString(500).Nullable()

            // Standard audit + version.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PickTasks_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PickTasks_UpdatedBy", "security", "Users", "Id")
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Index("IX_PickTasks_Status")
            .OnTable("PickTasks").InSchema("outbound")
            .OnColumn("Status").Ascending()
            .OnColumn("GeneratedAt").Descending();

        Create.Index("IX_PickTasks_SalesOrder")
            .OnTable("PickTasks").InSchema("outbound")
            .OnColumn("SalesOrderId").Ascending();

        Create.Index("IX_PickTasks_AssignedTo")
            .OnTable("PickTasks").InSchema("outbound")
            .OnColumn("AssignedTo").Ascending()
            .OnColumn("Status").Ascending();

        Execute.Sql(
            "ALTER TABLE [outbound].[PickTasks] " +
            "ADD CONSTRAINT CK_PickTasks_Status " +
            "CHECK (Status IN (" +
                "'Pending', 'InProgress', 'Picked', 'PartiallyPicked', 'Cancelled'));");

        // Per-state audit invariant. Mirrors Phase 11A/12/13 +
        // OrderAllocations CK_*_AuditMatchesStatus pattern.
        Execute.Sql(@"
ALTER TABLE [outbound].[PickTasks]
ADD CONSTRAINT CK_PickTasks_AuditMatchesStatus
CHECK (
    (Status = 'Pending'         AND StartedAt IS NULL AND CompletedAt IS NULL AND CancelledAt IS NULL)
 OR (Status = 'InProgress'      AND StartedAt IS NOT NULL AND CompletedAt IS NULL AND CancelledAt IS NULL)
 OR (Status = 'Picked'          AND CompletedAt IS NOT NULL AND CancelledAt IS NULL)
 OR (Status = 'PartiallyPicked' AND CompletedAt IS NOT NULL AND CancelledAt IS NULL)
 OR (Status = 'Cancelled'       AND CancelledAt IS NOT NULL AND CompletedAt IS NULL)
);");

        // ====================================================
        // PickTaskLines
        // ====================================================
        Create.Table("PickTaskLines").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // CASCADE: lines belong to the task. Cancelled tasks could
            // be hard-deleted via this path.
            .WithColumn("PickTaskId").AsGuid().NotNullable()
                .ForeignKey("FK_PickTaskLines_PickTask",
                            "outbound", "PickTasks", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("LineNumber").AsInt32().NotNullable()

            // FK to OrderAllocation — task line consumes this exact
            // allocation. NO ACTION on delete preserves history when
            // the SO line gets retired (rare; SO lines live forever
            // once allocated).
            .WithColumn("OrderAllocationId").AsGuid().NotNullable()
                .ForeignKey("FK_PickTaskLines_OrderAllocation",
                            "outbound", "OrderAllocations", "Id")

            // Snapshot fields (denormalized at task generation for
            // stable display + reporting). Mirrors Phase 12 cycle-
            // count line snapshots.
            .WithColumn("StockId").AsGuid().NotNullable()
                .ForeignKey("FK_PickTaskLines_Stock", "inventory", "Stock", "Id")
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_PickTaskLines_Product", "master", "Products", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_PickTaskLines_Owner", "master", "Owners", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_PickTaskLines_Uom", "master", "UnitsOfMeasure", "Id")
            .WithColumn("LocationId").AsGuid().NotNullable()
                .ForeignKey("FK_PickTaskLines_Location", "master", "Locations", "Id")
            .WithColumn("LotId").AsGuid().Nullable()
                .ForeignKey("FK_PickTaskLines_Lot", "inventory", "Lots", "Id")
            .WithColumn("PalletId").AsGuid().Nullable()
                .ForeignKey("FK_PickTaskLines_Pallet", "inventory", "Pallets", "Id")

            // Quantity progression.
            .WithColumn("ExpectedQuantity").AsDecimal(18, 4).NotNullable()  // = OrderAllocation.AllocatedQuantity at gen
            .WithColumn("PickedQuantity").AsDecimal(18, 4).Nullable()        // populated on submit; null = not picked yet

            // Per-line status: 'Pending' before submit, 'Picked' or
            // 'Skipped' after. Skipped = operator marked the line as
            // unpickable (full short).
            .WithColumn("LineStatus").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Pending")

            // Required when PickedQty < Expected (short-pick) — operator
            // notes why (out of stock / damaged / cycle-count needed).
            .WithColumn("ShortPickReason").AsString(500).Nullable()

            .WithColumn("Notes").AsString(500).Nullable()

            // Standard audit (no Version on lines — same convention as
            // CycleCountLines + TransferOrderLines).
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PickTaskLines_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PickTaskLines_UpdatedBy", "security", "Users", "Id");

        Create.Index("UX_PickTaskLines_Task_LineNumber")
            .OnTable("PickTaskLines").InSchema("outbound")
            .WithOptions().Unique()
            .OnColumn("PickTaskId").Ascending()
            .OnColumn("LineNumber").Ascending();

        Create.Index("IX_PickTaskLines_Allocation")
            .OnTable("PickTaskLines").InSchema("outbound")
            .OnColumn("OrderAllocationId").Ascending();

        Execute.Sql(
            "ALTER TABLE [outbound].[PickTaskLines] " +
            "ADD CONSTRAINT CK_PickTaskLines_LineStatus " +
            "CHECK (LineStatus IN ('Pending', 'Picked', 'Skipped'));");

        // Quantity invariants.
        Execute.Sql(
            "ALTER TABLE [outbound].[PickTaskLines] " +
            "ADD CONSTRAINT CK_PickTaskLines_Expected_Positive " +
            "CHECK (ExpectedQuantity > 0);");

        Execute.Sql(
            "ALTER TABLE [outbound].[PickTaskLines] " +
            "ADD CONSTRAINT CK_PickTaskLines_Picked_NonNegative " +
            "CHECK (PickedQuantity IS NULL OR PickedQuantity >= 0);");

        // Operator can't claim more picked than expected. Equal or less.
        Execute.Sql(
            "ALTER TABLE [outbound].[PickTaskLines] " +
            "ADD CONSTRAINT CK_PickTaskLines_PickedNotOverExpected " +
            "CHECK (PickedQuantity IS NULL OR PickedQuantity <= ExpectedQuantity);");

        // Per-line status invariant: populated qty must match status.
        Execute.Sql(@"
ALTER TABLE [outbound].[PickTaskLines]
ADD CONSTRAINT CK_PickTaskLines_StatusMatchesQty
CHECK (
    (LineStatus = 'Pending' AND PickedQuantity IS NULL)
 OR (LineStatus = 'Picked'  AND PickedQuantity IS NOT NULL)
 OR (LineStatus = 'Skipped' AND PickedQuantity IS NULL)
);");
    }

    public override void Down()
    {
        Delete.Table("PickTaskLines").InSchema("outbound");
        Delete.Table("PickTasks").InSchema("outbound");
    }
}
