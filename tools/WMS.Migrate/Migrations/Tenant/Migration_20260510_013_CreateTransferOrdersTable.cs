using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 13 (ADR-012) — inter-warehouse Transfer header.
//
// Workflow (v1 collapsed from ADR's 9-state, deferring PickTask
// integration to Phase 14+ Outbound):
//   Draft → Submitted → Approved → InTransit → Received  (terminal happy)
//                ↓        ↓           ↓
//            Cancelled  Cancelled  Lost                   (terminal failure)
//
// In-transit handling per ADR Option B: NO stock record. Source stock
// decrements on Dispatch (Approved → InTransit); dest stock increments
// on Receive (InTransit → Received). The line table tracks
// QtyDispatched / QtyReceived; QtyLossInTransit is a computed column.
//
// Owner preserved per line (3PL/VMI invariant); lot preserved when
// specified.
//
// Skipped from ADR for v1 (see TD logged at Phase 13 close):
//   - inventory.TransferStatusHistory (full per-transition audit)
//   - PickTaskId / AdjustmentId on lines (Pick + auto-Adjustment)
//   - CarrierId / TrackingNumber / Priority / EstimatedTransitDays
//   - Mobile receiving workflow
[Migration(20260510013L)]
[Tags("Tenant")]
public class Migration_20260510_013_CreateTransferOrdersTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("TransferOrders").InSchema("inventory")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            .WithColumn("TransferNumber").AsAnsiString(50).NotNullable().Unique()

            .WithColumn("FromWarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrders_FromWarehouse",
                            "master", "Warehouses", "Id")
            .WithColumn("ToWarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrders_ToWarehouse",
                            "master", "Warehouses", "Id")

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Draft")

            .WithColumn("Reason").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Other")

            .WithColumn("Notes").AsString(1000).Nullable()

            // Per-state audit trio. RequestedBy / At = creator (always set).
            .WithColumn("RequestedBy").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrders_RequestedBy", "security", "Users", "Id")
            .WithColumn("RequestedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))

            .WithColumn("SubmittedBy").AsGuid().Nullable()
                .ForeignKey("FK_TransferOrders_SubmittedBy", "security", "Users", "Id")
            .WithColumn("SubmittedAt").AsDateTime2().Nullable()

            .WithColumn("ApprovedBy").AsGuid().Nullable()
                .ForeignKey("FK_TransferOrders_ApprovedBy", "security", "Users", "Id")
            .WithColumn("ApprovedAt").AsDateTime2().Nullable()

            .WithColumn("DispatchedBy").AsGuid().Nullable()
                .ForeignKey("FK_TransferOrders_DispatchedBy", "security", "Users", "Id")
            .WithColumn("DispatchedAt").AsDateTime2().Nullable()

            .WithColumn("ReceivedBy").AsGuid().Nullable()
                .ForeignKey("FK_TransferOrders_ReceivedBy", "security", "Users", "Id")
            .WithColumn("ReceivedAt").AsDateTime2().Nullable()

            // Cancelled / Lost terminal states.
            .WithColumn("CancelledBy").AsGuid().Nullable()
                .ForeignKey("FK_TransferOrders_CancelledBy", "security", "Users", "Id")
            .WithColumn("CancelledAt").AsDateTime2().Nullable()
            .WithColumn("CancelReason").AsString(500).Nullable()

            .WithColumn("LostBy").AsGuid().Nullable()
                .ForeignKey("FK_TransferOrders_LostBy", "security", "Users", "Id")
            .WithColumn("LostAt").AsDateTime2().Nullable()
            .WithColumn("LossReason").AsString(500).Nullable()

            // Standard audit + version.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable()
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // List-page query indexes.
        Create.Index("IX_TransferOrders_Status")
            .OnTable("TransferOrders").InSchema("inventory")
            .OnColumn("Status").Ascending()
            .OnColumn("RequestedAt").Descending();

        Create.Index("IX_TransferOrders_Warehouses")
            .OnTable("TransferOrders").InSchema("inventory")
            .OnColumn("FromWarehouseId").Ascending()
            .OnColumn("ToWarehouseId").Ascending()
            .OnColumn("Status").Ascending();

        Execute.Sql(@"
ALTER TABLE inventory.TransferOrders
ADD CONSTRAINT CK_TransferOrders_Status
CHECK (Status IN ('Draft','Submitted','Approved','InTransit','Received','Cancelled','Lost'));");

        Execute.Sql(@"
ALTER TABLE inventory.TransferOrders
ADD CONSTRAINT CK_TransferOrders_Reason
CHECK (Reason IN ('Rebalance','Replenish','CustomerOrder','Other'));");

        // Cross-warehouse transfer — From must differ from To.
        Execute.Sql(@"
ALTER TABLE inventory.TransferOrders
ADD CONSTRAINT CK_TransferOrders_FromTo
CHECK (FromWarehouseId <> ToWarehouseId);");

        // Per-state audit invariant. Mirrors Phase 11A's
        // CK_Adjustments_AuditMatchesStatus and Phase 12's
        // CK_CycleCounts_AuditMatchesStatus shape.
        Execute.Sql(@"
ALTER TABLE inventory.TransferOrders
ADD CONSTRAINT CK_TransferOrders_AuditMatchesStatus
CHECK (
    (Status = 'Draft'      AND SubmittedAt IS NULL AND ApprovedAt IS NULL AND DispatchedAt IS NULL AND ReceivedAt IS NULL AND CancelledAt IS NULL AND LostAt IS NULL)
 OR (Status = 'Submitted'  AND SubmittedAt IS NOT NULL AND ApprovedAt IS NULL AND DispatchedAt IS NULL AND ReceivedAt IS NULL AND CancelledAt IS NULL AND LostAt IS NULL)
 OR (Status = 'Approved'   AND SubmittedAt IS NOT NULL AND ApprovedAt IS NOT NULL AND DispatchedAt IS NULL AND ReceivedAt IS NULL AND CancelledAt IS NULL AND LostAt IS NULL)
 OR (Status = 'InTransit'  AND SubmittedAt IS NOT NULL AND ApprovedAt IS NOT NULL AND DispatchedAt IS NOT NULL AND ReceivedAt IS NULL AND CancelledAt IS NULL AND LostAt IS NULL)
 OR (Status = 'Received'   AND SubmittedAt IS NOT NULL AND ApprovedAt IS NOT NULL AND DispatchedAt IS NOT NULL AND ReceivedAt IS NOT NULL AND CancelledAt IS NULL AND LostAt IS NULL)
 OR (Status = 'Cancelled'  AND CancelledAt IS NOT NULL AND ReceivedAt IS NULL AND LostAt IS NULL)
 OR (Status = 'Lost'       AND LostAt IS NOT NULL AND ReceivedAt IS NULL AND CancelledAt IS NULL)
);");
    }

    public override void Down() =>
        Delete.Table("TransferOrders").InSchema("inventory");
}
