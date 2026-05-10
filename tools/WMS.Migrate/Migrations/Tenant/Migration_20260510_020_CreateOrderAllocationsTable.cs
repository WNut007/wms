using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14B — outbound.OrderAllocations. Per-line linkage from a
// SalesOrderLine to a specific inventory.Stock row, with the qty
// reserved against that row. One SO line typically maps to one or a
// few Stock rows (FIFO might split across lots; future FEFO across
// expiry batches).
//
// Status flow: 'Active' on insert; flips to 'Released' on cancel /
// allocation reversal. No hard delete — audit-grade traceability of
// what stock was reserved when, by whom, and why released. (Pick +
// Ship states arrive in 14C/D.)
//
// Owner-aware: ADR-007 invariant enforced at service layer — the
// (Product, Owner, UoM) on the SalesOrderLine must match the same
// tuple on the targeted Stock row. Don't FK-constrain those across
// here; the join chain is implicit through Line→Stock.
//
// CASCADE on SalesOrderLineId: when a Draft SO's lines get DELETED via
// ReplaceLinesAsync, no orphan allocations possible (Draft SOs have
// none anyway, but defensive). NO ACTION on StockId: a retired Stock
// row mustn't disappear silently — the allocation history pins it.
[Migration(20260510020L)]
[Tags("Tenant")]
public class Migration_20260510_020_CreateOrderAllocationsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("OrderAllocations").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            .WithColumn("SalesOrderLineId").AsGuid().NotNullable()
                .ForeignKey("FK_OrderAllocations_SalesOrderLine",
                            "outbound", "SalesOrderLines", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("StockId").AsGuid().NotNullable()
                .ForeignKey("FK_OrderAllocations_Stock",
                            "inventory", "Stock", "Id")

            .WithColumn("AllocatedQuantity").AsDecimal(18, 4).NotNullable()

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Active")

            // Active-state audit trio.
            .WithColumn("AllocatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("AllocatedBy").AsGuid().Nullable()
                .ForeignKey("FK_OrderAllocations_AllocatedBy", "security", "Users", "Id")

            // Released-state audit trio. Populated when the allocation
            // is reversed (cancel SO, or future Pick-failed flow).
            .WithColumn("ReleasedAt").AsDateTime2().Nullable()
            .WithColumn("ReleasedBy").AsGuid().Nullable()
                .ForeignKey("FK_OrderAllocations_ReleasedBy", "security", "Users", "Id")
            .WithColumn("ReleaseReason").AsString(500).Nullable()

            // Standard audit. CreatedBy mirrors AllocatedBy in practice
            // but kept distinct so the BaseEntity convention applies.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_OrderAllocations_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_OrderAllocations_UpdatedBy", "security", "Users", "Id");

        // Per-line active-allocation lookup — drives the Detail Lines
        // panel's allocation breakdown ("show me all current allocations
        // for this SO line").
        Create.Index("IX_OrderAllocations_Line")
            .OnTable("OrderAllocations").InSchema("outbound")
            .OnColumn("SalesOrderLineId").Ascending()
            .OnColumn("Status").Ascending();

        // Reverse lookup — "which SO lines are reserving this Stock
        // row?". Useful for the future inventory.Stock detail page +
        // allocation reversal queries.
        Create.Index("IX_OrderAllocations_Stock")
            .OnTable("OrderAllocations").InSchema("outbound")
            .OnColumn("StockId").Ascending()
            .OnColumn("Status").Ascending();

        // Status enum guard.
        Execute.Sql(
            "ALTER TABLE [outbound].[OrderAllocations] " +
            "ADD CONSTRAINT CK_OrderAllocations_Status " +
            "CHECK (Status IN ('Active', 'Released'));");

        // Quantity invariant — must allocate something positive. Zero-
        // qty allocation is a data bug.
        Execute.Sql(
            "ALTER TABLE [outbound].[OrderAllocations] " +
            "ADD CONSTRAINT CK_OrderAllocations_Quantity_Positive " +
            "CHECK (AllocatedQuantity > 0);");

        // Per-status audit invariant. Mirrors the Phase 11A/12/13
        // CK_*_AuditMatchesStatus pattern (see
        // feedback_audit_status_invariant_ck.md).
        Execute.Sql(@"
ALTER TABLE [outbound].[OrderAllocations]
ADD CONSTRAINT CK_OrderAllocations_AuditMatchesStatus
CHECK (
    (Status = 'Active'   AND ReleasedAt IS NULL     AND ReleasedBy IS NULL)
 OR (Status = 'Released' AND ReleasedAt IS NOT NULL)
);");
    }

    public override void Down() =>
        Delete.Table("OrderAllocations").InSchema("outbound");
}
