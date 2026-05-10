using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14E — outbound.Shipments header. One shipment per SO for MVP
// (UX_Shipments_SalesOrder UNIQUE enforces).
//
// State flow (3-state, mirrors 14D Pack):
//   Pending → Shipped | Cancelled
//
// Pending   = shipment generated; operator hasn't submitted yet
// Shipped   = submitted; CarrierName + TrackingNumber stamped, SO
//             flipped Packed → Shipped, all the SO's cartons stamped
//             with this ShipmentId
// Cancelled = pre-Submit reversal; SO stays Packed (Generate didn't
//             flip it, so Cancel doesn't either)
//
// Carrier handling: free-text VARCHAR(50). The codebase has a full
// master.Carriers table + 4 seeded carriers (FLASH/KERRY/JT/THAIPOST)
// but ALL 'Inactive' status; the existing GetActiveAsync filters
// Production-only and would render an empty dropdown in dev. FK
// integration is a TD for v2.x once admins promote carriers.
//
// TrackingNumber: nullable VARCHAR(100). Operator may not have a
// tracking number at ship time (deferred-default carrier pattern per
// CLAUDE.md ADR-009 sketch).
//
// Per-state audit trio (CLAUDE.md "Audit Field FK Rules" pattern,
// mirroring Phase 11A/12/13/14B/14C/14D):
//   GeneratedAt/By  — always set on insert
//   ShippedAt/By    — set when status flips Pending → Shipped
//   CancelledAt/By + CancelReason — set when Pending → Cancelled
//
// CK_Shipments_AuditMatchesStatus enforces the per-state invariant.
[Migration(20260510030L)]
[Tags("Tenant")]
public class Migration_20260510_030_CreateShipmentsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Shipments").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // SHP-YYYYMMDD-NNNN. Tenant-wide unique.
            .WithColumn("ShipmentNumber").AsAnsiString(50).NotNullable().Unique()

            .WithColumn("SalesOrderId").AsGuid().NotNullable()
                .ForeignKey("FK_Shipments_SalesOrder",
                            "outbound", "SalesOrders", "Id")

            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Pending")

            // Free-text carrier label for MVP. Lookup integration to
            // master.Carriers is a future TD.
            .WithColumn("CarrierName").AsAnsiString(50).Nullable()

            // Free-text tracking number (operator may not know at ship
            // time — deferred-default carrier pattern).
            .WithColumn("TrackingNumber").AsAnsiString(100).Nullable()

            .WithColumn("Notes").AsString(1000).Nullable()

            // Per-state audit trio.
            .WithColumn("GeneratedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("GeneratedBy").AsGuid().Nullable()
                .ForeignKey("FK_Shipments_GeneratedBy", "security", "Users", "Id")

            .WithColumn("ShippedAt").AsDateTime2().Nullable()
            .WithColumn("ShippedBy").AsGuid().Nullable()
                .ForeignKey("FK_Shipments_ShippedBy", "security", "Users", "Id")

            .WithColumn("CancelledAt").AsDateTime2().Nullable()
            .WithColumn("CancelledBy").AsGuid().Nullable()
                .ForeignKey("FK_Shipments_CancelledBy", "security", "Users", "Id")
            .WithColumn("CancelReason").AsString(500).Nullable()

            // Standard audit + version.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_Shipments_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_Shipments_UpdatedBy", "security", "Users", "Id")
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Enforces 1:1 SO → Shipment for MVP. Drop this in a future
        // migration to enable multi-shipment splitting.
        Create.Index("UX_Shipments_SalesOrder")
            .OnTable("Shipments").InSchema("outbound")
            .WithOptions().Unique()
            .OnColumn("SalesOrderId").Ascending();

        Create.Index("IX_Shipments_Status")
            .OnTable("Shipments").InSchema("outbound")
            .OnColumn("Status").Ascending()
            .OnColumn("GeneratedAt").Descending();

        Execute.Sql(
            "ALTER TABLE [outbound].[Shipments] " +
            "ADD CONSTRAINT CK_Shipments_Status " +
            "CHECK (Status IN ('Pending', 'Shipped', 'Cancelled'));");

        // Per-state audit invariant. Mirrors Phase 11A/12/13/14B/14C/14D
        // *_AuditMatchesStatus pattern.
        Execute.Sql(@"
ALTER TABLE [outbound].[Shipments]
ADD CONSTRAINT CK_Shipments_AuditMatchesStatus
CHECK (
    (Status = 'Pending'   AND ShippedAt IS NULL     AND CancelledAt IS NULL)
 OR (Status = 'Shipped'   AND ShippedAt IS NOT NULL AND CancelledAt IS NULL)
 OR (Status = 'Cancelled' AND CancelledAt IS NOT NULL AND ShippedAt IS NULL)
);");
    }

    public override void Down()
    {
        Delete.Table("Shipments").InSchema("outbound");
    }
}
