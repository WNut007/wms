using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 13 (ADR-012) — TransferOrder line items.
//
// Each line carries the per-product/owner/uom/lot/pallet movement
// detail. Owner preserved (3PL/VMI invariant). Lot preserved when
// specified (no commingling per ADR).
//
// Quantities flow through three states:
//   QtyRequested  — captured on Create (operator's intent)
//   QtyDispatched — populated on Dispatch (operator's actual pick)
//   QtyReceived   — populated on Receive (dest operator's count)
//
// QtyLossInTransit is a PERSISTED computed column —
// (ISNULL(QtyDispatched,0) - ISNULL(QtyReceived,0)). Negative value
// would indicate over-receive (rare; rejected at service layer for
// v1).
//
// Per-line Status:
//   'Pending'    — created, awaiting Dispatch
//   'Dispatched' — picked + sent (QtyDispatched populated)
//   'Received'   — arrived (QtyReceived populated, no variance)
//   'Variance'   — arrived with QtyLossInTransit > 0 (loss in transit)
//
// FromLocationId and ToLocationId are NOT NULL for v1 (operator
// picks both at Create time). ADR allows NULL (= "any source" /
// "auto-putaway"); deferred to TD-XXX.
[Migration(20260510014L)]
[Tags("Tenant")]
public class Migration_20260510_014_CreateTransferOrderLinesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("TransferOrderLines").InSchema("inventory")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            .WithColumn("TransferId").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrderLines_Transfer",
                            "inventory", "TransferOrders", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("LineNumber").AsInt32().NotNullable()

            // Owner + Lot preserved per ADR-007 / ADR-012.
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrderLines_Product", "master", "Products", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrderLines_Owner", "master", "Owners", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrderLines_Uom", "master", "UnitsOfMeasure", "Id")
            .WithColumn("LotId").AsGuid().Nullable()
                .ForeignKey("FK_TransferOrderLines_Lot", "inventory", "Lots", "Id")
            .WithColumn("PalletId").AsGuid().Nullable()
                .ForeignKey("FK_TransferOrderLines_Pallet", "inventory", "Pallets", "Id")

            // Source + destination locations — v1 both required.
            .WithColumn("FromLocationId").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrderLines_FromLocation", "master", "Locations", "Id")
            .WithColumn("ToLocationId").AsGuid().NotNullable()
                .ForeignKey("FK_TransferOrderLines_ToLocation", "master", "Locations", "Id")

            // Quantity progression.
            .WithColumn("QtyRequested").AsDecimal(18, 4).NotNullable()
            .WithColumn("QtyDispatched").AsDecimal(18, 4).Nullable()
            .WithColumn("QtyReceived").AsDecimal(18, 4).Nullable()
            // QtyLossInTransit added below as computed column.

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Pending")

            .WithColumn("Notes").AsString(500).Nullable()

            // Standard audit (no Version on lines — same convention as
            // CycleCountLines).
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // PERSISTED computed column — must be added via raw SQL
        // (FluentMigrator has no fluent helper).
        Execute.Sql(@"
ALTER TABLE inventory.TransferOrderLines
ADD QtyLossInTransit AS (ISNULL(QtyDispatched,0) - ISNULL(QtyReceived,0)) PERSISTED;");

        Create.Index("IX_TransferOrderLines_Transfer")
            .OnTable("TransferOrderLines").InSchema("inventory")
            .OnColumn("TransferId").Ascending()
            .OnColumn("LineNumber").Ascending();

        Create.Index("IX_TransferOrderLines_Product")
            .OnTable("TransferOrderLines").InSchema("inventory")
            .OnColumn("ProductId").Ascending()
            .OnColumn("Status").Ascending();

        Execute.Sql(@"
ALTER TABLE inventory.TransferOrderLines
ADD CONSTRAINT CK_TransferOrderLines_Status
CHECK (Status IN ('Pending','Dispatched','Received','Variance'));");

        Execute.Sql(@"
ALTER TABLE inventory.TransferOrderLines
ADD CONSTRAINT CK_TransferOrderLines_QtyRequested_Positive
CHECK (QtyRequested > 0);");

        Execute.Sql(@"
ALTER TABLE inventory.TransferOrderLines
ADD CONSTRAINT CK_TransferOrderLines_QtyDispatched_NonNegative
CHECK (QtyDispatched IS NULL OR QtyDispatched >= 0);");

        Execute.Sql(@"
ALTER TABLE inventory.TransferOrderLines
ADD CONSTRAINT CK_TransferOrderLines_QtyReceived_NonNegative
CHECK (QtyReceived IS NULL OR QtyReceived >= 0);");

        // QtyDispatched can't exceed QtyRequested (no over-pick).
        Execute.Sql(@"
ALTER TABLE inventory.TransferOrderLines
ADD CONSTRAINT CK_TransferOrderLines_DispatchedNotOverRequested
CHECK (QtyDispatched IS NULL OR QtyDispatched <= QtyRequested);");

        // QtyReceived can't exceed QtyDispatched (no over-receive in v1).
        Execute.Sql(@"
ALTER TABLE inventory.TransferOrderLines
ADD CONSTRAINT CK_TransferOrderLines_ReceivedNotOverDispatched
CHECK (QtyReceived IS NULL OR (QtyDispatched IS NOT NULL AND QtyReceived <= QtyDispatched));");

        // Per-line status invariant — populated quantities match status.
        Execute.Sql(@"
ALTER TABLE inventory.TransferOrderLines
ADD CONSTRAINT CK_TransferOrderLines_StatusMatchesQty
CHECK (
    (Status = 'Pending'    AND QtyDispatched IS NULL AND QtyReceived IS NULL)
 OR (Status = 'Dispatched' AND QtyDispatched IS NOT NULL AND QtyReceived IS NULL)
 OR (Status = 'Received'   AND QtyDispatched IS NOT NULL AND QtyReceived IS NOT NULL)
 OR (Status = 'Variance'   AND QtyDispatched IS NOT NULL AND QtyReceived IS NOT NULL)
);");
    }

    public override void Down() =>
        Delete.Table("TransferOrderLines").InSchema("inventory");
}
