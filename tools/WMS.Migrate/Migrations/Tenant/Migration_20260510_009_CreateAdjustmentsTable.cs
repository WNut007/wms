using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 11A (ADR-013) — General Stock Adjustment header table.
//
// Single-line adjustments per design Q1; multi-line cycle counts use
// the future counts.CountAdjustments table per ADR-013. Workflow is
// flat 3-state Pending → (Applied | Rejected) — Apply happens
// atomically as part of approval, no intermediate Approved state.
//
// 6-tuple stock target denormalised (LocationId / ProductId / LotId /
// PalletId / OwnerId / UomId + WarehouseId for fast filter). StockId
// nullable: NULL on Pending when the adjustment will create a new
// stock row at the key; populated to the actual row on Apply.
//
// All Cancelled* / Approved* / Rejected* audit columns nullable;
// terminal-state CHECK at the end enforces "Pending has no audit /
// Applied has approval+apply / Rejected has rejection".
//
// FK CreatedBy / UpdatedBy / RequestedBy / ApprovedBy / RejectedBy
// → security.Users with NO ACTION (per CLAUDE.md audit FK rules).
[Migration(20260510009L)]
[Tags("Tenant")]
public class Migration_20260510_009_CreateAdjustmentsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Adjustments").InSchema("inventory")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Tenant-wide unique number — printed on the request slip,
            // searched in Index. Format ADJ-YYYYMMDD-NNNN assigned by
            // the service.
            .WithColumn("AdjustmentNumber").AsAnsiString(50).NotNullable().Unique()

            // Stock 6-tuple — denormalised so an Adjustment row is
            // self-describing in the audit log even after the underlying
            // Stock row is deleted (which shouldn't happen but defensive).
            // FKs prevent the underlying master rows being deleted while
            // an adjustment references them.
            .WithColumn("StockId").AsGuid().Nullable()
                .ForeignKey("FK_Adjustments_Stock", "inventory", "Stock", "Id")

            .WithColumn("LocationId").AsGuid().NotNullable()
                .ForeignKey("FK_Adjustments_Location", "master", "Locations", "Id")
            .WithColumn("ProductId").AsGuid().NotNullable()
                .ForeignKey("FK_Adjustments_Product", "master", "Products", "Id")
            .WithColumn("LotId").AsGuid().Nullable()
                .ForeignKey("FK_Adjustments_Lot", "inventory", "Lots", "Id")
            .WithColumn("PalletId").AsGuid().Nullable()
                .ForeignKey("FK_Adjustments_Pallet", "inventory", "Pallets", "Id")
            .WithColumn("OwnerId").AsGuid().NotNullable()
                .ForeignKey("FK_Adjustments_Owner", "master", "Owners", "Id")
            .WithColumn("UomId").AsGuid().NotNullable()
                .ForeignKey("FK_Adjustments_Uom", "master", "UnitsOfMeasure", "Id")
            .WithColumn("WarehouseId").AsGuid().NotNullable()
                .ForeignKey("FK_Adjustments_Warehouse", "master", "Warehouses", "Id")

            // Signed delta. CHECK forbids 0 (no-op adjustment).
            .WithColumn("QuantityDelta").AsDecimal(18, 4).NotNullable()

            // Closed-list reason; CHECK constraint at the end. Free-text
            // Notes always allowed for context.
            .WithColumn("Reason").AsAnsiString(50).NotNullable()
            .WithColumn("Notes").AsString(1000).Nullable()

            // Status workflow — 3 terminal states, no Draft.
            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Pending")

            // Audit trio per state. RequestedBy is NOT NULL because
            // every adjustment has a creator; the rest populate as the
            // workflow advances.
            .WithColumn("RequestedBy").AsGuid().NotNullable()
                .ForeignKey("FK_Adjustments_RequestedBy", "security", "Users", "Id")
            .WithColumn("RequestedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("ApprovedBy").AsGuid().Nullable()
                .ForeignKey("FK_Adjustments_ApprovedBy", "security", "Users", "Id")
            .WithColumn("ApprovedAt").AsDateTime2().Nullable()
            .WithColumn("AppliedAt").AsDateTime2().Nullable()
            .WithColumn("RejectedBy").AsGuid().Nullable()
                .ForeignKey("FK_Adjustments_RejectedBy", "security", "Users", "Id")
            .WithColumn("RejectedAt").AsDateTime2().Nullable()
            .WithColumn("RejectionReason").AsString(500).Nullable()

            // Standard audit + version.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable()
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Pending queue — primary "what needs my approval" view.
        Create.Index("IX_Adjustments_Status")
            .OnTable("Adjustments").InSchema("inventory")
            .OnColumn("Status").Ascending()
            .OnColumn("RequestedAt").Descending();

        // Per-warehouse list filter.
        Create.Index("IX_Adjustments_Warehouse")
            .OnTable("Adjustments").InSchema("inventory")
            .OnColumn("WarehouseId").Ascending()
            .OnColumn("RequestedAt").Descending();

        // "All adjustments for this stock row" — Stock detail page
        // future use. Filtered partial index skips Pending rows whose
        // StockId is still NULL.
        Execute.Sql(@"
CREATE INDEX IX_Adjustments_Stock
    ON inventory.Adjustments (StockId)
    WHERE StockId IS NOT NULL;");

        // CHECK constraints — one per closed enum + workflow invariant.
        Execute.Sql(@"
ALTER TABLE inventory.Adjustments
ADD CONSTRAINT CK_Adjustments_Status
CHECK (Status IN ('Pending', 'Applied', 'Rejected'));");

        Execute.Sql(@"
ALTER TABLE inventory.Adjustments
ADD CONSTRAINT CK_Adjustments_Reason
CHECK (Reason IN (
    'Damaged', 'Expired', 'Lost', 'Found',
    'ReturnedToSupplier', 'Sample', 'Other'));");

        Execute.Sql(@"
ALTER TABLE inventory.Adjustments
ADD CONSTRAINT CK_Adjustments_QuantityDelta_NonZero
CHECK (QuantityDelta <> 0);");

        // Workflow invariant — terminal-state audit trio matches Status.
        // Pending: no approval / apply / rejection audit.
        // Applied: approval + apply audit, no rejection.
        // Rejected: rejection audit, no approval / apply.
        Execute.Sql(@"
ALTER TABLE inventory.Adjustments
ADD CONSTRAINT CK_Adjustments_AuditMatchesStatus
CHECK (
    (Status = 'Pending'  AND ApprovedAt IS NULL AND AppliedAt IS NULL AND RejectedAt IS NULL)
 OR (Status = 'Applied'  AND ApprovedAt IS NOT NULL AND AppliedAt IS NOT NULL AND RejectedAt IS NULL)
 OR (Status = 'Rejected' AND RejectedAt IS NOT NULL AND AppliedAt IS NULL)
);");
    }

    public override void Down() =>
        Delete.Table("Adjustments").InSchema("inventory");
}
