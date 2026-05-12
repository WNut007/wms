using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Block 4.5.1 chunk c — adds LineNo (gap-10 sequence, immutable after
// INSERT) + DisplayOrder (drag-drop UI position, mutable) across the
// 5 line tables that need them. Foundation for the PO Lines grid
// landing in Block 4.5.2.
//
// COLUMN ROLES:
//
//   LineNo INT NOT NULL — gap-10 sequence (10, 20, 30, ...). Server-
//     generated on INSERT, immutable thereafter. Used by print +
//     supplier-facing documents — deterministic order survives
//     reorders + future inserts-between-gaps. Capacity = 999 lines/
//     document (LineNo max = 9990, headroom to 9999). When gaps
//     deplete, application layer enqueues RenumberLinesJob (Block
//     4.5.4). NOT enforced by DB CHECK — keeps backfill simple and
//     keeps capacity policy at the application layer where the
//     RenumberLinesJob lives.
//
//   DisplayOrder INT NOT NULL — UI grid position. Initial value =
//     LineNumber (preserves existing display order). Mutable via
//     drag-drop AJAX (Block 4.5.2). UI grids ORDER BY this; mobile
//     pickers ORDER BY this for optimal pick path.
//
// EXISTING LineNumber column is PRESERVED (Option X per Block 4.5.1
// pre-flight). Existing repos / Razor views keep reading it unchanged.
// New code (the grid) hides LineNumber and operates on LineNo +
// DisplayOrder. TD logged to deprecate LineNumber once all consumers
// migrate to LineNo.
//
// VERSION COLUMN SPLIT — relevant to Block 4.5.2's Reorder endpoint:
//   Has Version (optimistic concurrency on UPDATE):
//     - inbound.PurchaseOrderLines
//     - inbound.ReceivingLines
//     - outbound.SalesOrderLines
//   No Version (operator-overwrite-freely pattern):
//     - counts.CycleCountLines  (operator may overwrite during Counting)
//     - inventory.TransferOrderLines  (matches CycleCount convention)
//
//   Reorder UPDATE on the 3 with Version MUST bump Version by 1 with
//   a WHERE Version = @from concurrency guard. Reorder UPDATE on the
//   2 without Version is unconditional. Block 4.5.2 ships PO Lines
//   first; the other surfaces follow at rule-of-three refactor time.
//
// BACKFILL — one UPDATE per table:
//     SET LineNo = LineNumber * 10, DisplayOrder = LineNumber
//   For documents with up to 999 lines, LineNo lands in [10, 9990].
//   No tenant has > 999-line documents in practice (test fixtures
//   max ~5 lines; demo seed ~3-10).
//
// INDEXES — 2 per table:
//   IX_{Table}_{ParentAbbr}_DisplayOrder — grid query (UI ORDER BY)
//   IX_{Table}_{ParentAbbr}_LineNo       — print query (doc ORDER BY)
[Migration(20260518037L)]
[Tags("Tenant")]
public class Migration_20260518_037_AddLineNoAndDisplayOrderToLineTables : MigrationBase
{
    public override void Up()
    {
        // ===== inbound.PurchaseOrderLines =====
        Alter.Table("PurchaseOrderLines").InSchema("inbound")
            .AddColumn("LineNo").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("DisplayOrder").AsInt32().NotNullable().WithDefaultValue(0);
        Execute.Sql(
            "UPDATE [inbound].[PurchaseOrderLines] " +
            "SET [LineNo] = LineNumber * 10, DisplayOrder = LineNumber;");
        Create.Index("IX_PurchaseOrderLines_Po_DisplayOrder")
            .OnTable("PurchaseOrderLines").InSchema("inbound")
            .OnColumn("PurchaseOrderId").Ascending()
            .OnColumn("DisplayOrder").Ascending();
        Create.Index("IX_PurchaseOrderLines_Po_LineNo")
            .OnTable("PurchaseOrderLines").InSchema("inbound")
            .OnColumn("PurchaseOrderId").Ascending()
            .OnColumn("LineNo").Ascending();

        // ===== inbound.ReceivingLines =====
        Alter.Table("ReceivingLines").InSchema("inbound")
            .AddColumn("LineNo").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("DisplayOrder").AsInt32().NotNullable().WithDefaultValue(0);
        Execute.Sql(
            "UPDATE [inbound].[ReceivingLines] " +
            "SET [LineNo] = LineNumber * 10, DisplayOrder = LineNumber;");
        Create.Index("IX_ReceivingLines_Header_DisplayOrder")
            .OnTable("ReceivingLines").InSchema("inbound")
            .OnColumn("ReceivingHeaderId").Ascending()
            .OnColumn("DisplayOrder").Ascending();
        Create.Index("IX_ReceivingLines_Header_LineNo")
            .OnTable("ReceivingLines").InSchema("inbound")
            .OnColumn("ReceivingHeaderId").Ascending()
            .OnColumn("LineNo").Ascending();

        // ===== outbound.SalesOrderLines =====
        Alter.Table("SalesOrderLines").InSchema("outbound")
            .AddColumn("LineNo").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("DisplayOrder").AsInt32().NotNullable().WithDefaultValue(0);
        Execute.Sql(
            "UPDATE [outbound].[SalesOrderLines] " +
            "SET [LineNo] = LineNumber * 10, DisplayOrder = LineNumber;");
        Create.Index("IX_SalesOrderLines_So_DisplayOrder")
            .OnTable("SalesOrderLines").InSchema("outbound")
            .OnColumn("SalesOrderId").Ascending()
            .OnColumn("DisplayOrder").Ascending();
        Create.Index("IX_SalesOrderLines_So_LineNo")
            .OnTable("SalesOrderLines").InSchema("outbound")
            .OnColumn("SalesOrderId").Ascending()
            .OnColumn("LineNo").Ascending();

        // ===== counts.CycleCountLines =====
        Alter.Table("CycleCountLines").InSchema("counts")
            .AddColumn("LineNo").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("DisplayOrder").AsInt32().NotNullable().WithDefaultValue(0);
        Execute.Sql(
            "UPDATE [counts].[CycleCountLines] " +
            "SET [LineNo] = LineNumber * 10, DisplayOrder = LineNumber;");
        Create.Index("IX_CycleCountLines_CycleCount_DisplayOrder")
            .OnTable("CycleCountLines").InSchema("counts")
            .OnColumn("CycleCountId").Ascending()
            .OnColumn("DisplayOrder").Ascending();
        Create.Index("IX_CycleCountLines_CycleCount_LineNo")
            .OnTable("CycleCountLines").InSchema("counts")
            .OnColumn("CycleCountId").Ascending()
            .OnColumn("LineNo").Ascending();

        // ===== inventory.TransferOrderLines =====
        Alter.Table("TransferOrderLines").InSchema("inventory")
            .AddColumn("LineNo").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("DisplayOrder").AsInt32().NotNullable().WithDefaultValue(0);
        Execute.Sql(
            "UPDATE [inventory].[TransferOrderLines] " +
            "SET [LineNo] = LineNumber * 10, DisplayOrder = LineNumber;");
        Create.Index("IX_TransferOrderLines_Transfer_DisplayOrder")
            .OnTable("TransferOrderLines").InSchema("inventory")
            .OnColumn("TransferId").Ascending()
            .OnColumn("DisplayOrder").Ascending();
        Create.Index("IX_TransferOrderLines_Transfer_LineNo")
            .OnTable("TransferOrderLines").InSchema("inventory")
            .OnColumn("TransferId").Ascending()
            .OnColumn("LineNo").Ascending();
    }

    public override void Down()
    {
        // Reverse-order teardown: indexes first, then columns. Per-table
        // order is reversed too so a partial Down() leaves a coherent
        // state if it fails halfway.

        // ===== inventory.TransferOrderLines =====
        Delete.Index("IX_TransferOrderLines_Transfer_LineNo")
            .OnTable("TransferOrderLines").InSchema("inventory");
        Delete.Index("IX_TransferOrderLines_Transfer_DisplayOrder")
            .OnTable("TransferOrderLines").InSchema("inventory");
        Delete.Column("DisplayOrder").FromTable("TransferOrderLines").InSchema("inventory");
        Delete.Column("LineNo").FromTable("TransferOrderLines").InSchema("inventory");

        // ===== counts.CycleCountLines =====
        Delete.Index("IX_CycleCountLines_CycleCount_LineNo")
            .OnTable("CycleCountLines").InSchema("counts");
        Delete.Index("IX_CycleCountLines_CycleCount_DisplayOrder")
            .OnTable("CycleCountLines").InSchema("counts");
        Delete.Column("DisplayOrder").FromTable("CycleCountLines").InSchema("counts");
        Delete.Column("LineNo").FromTable("CycleCountLines").InSchema("counts");

        // ===== outbound.SalesOrderLines =====
        Delete.Index("IX_SalesOrderLines_So_LineNo")
            .OnTable("SalesOrderLines").InSchema("outbound");
        Delete.Index("IX_SalesOrderLines_So_DisplayOrder")
            .OnTable("SalesOrderLines").InSchema("outbound");
        Delete.Column("DisplayOrder").FromTable("SalesOrderLines").InSchema("outbound");
        Delete.Column("LineNo").FromTable("SalesOrderLines").InSchema("outbound");

        // ===== inbound.ReceivingLines =====
        Delete.Index("IX_ReceivingLines_Header_LineNo")
            .OnTable("ReceivingLines").InSchema("inbound");
        Delete.Index("IX_ReceivingLines_Header_DisplayOrder")
            .OnTable("ReceivingLines").InSchema("inbound");
        Delete.Column("DisplayOrder").FromTable("ReceivingLines").InSchema("inbound");
        Delete.Column("LineNo").FromTable("ReceivingLines").InSchema("inbound");

        // ===== inbound.PurchaseOrderLines =====
        Delete.Index("IX_PurchaseOrderLines_Po_LineNo")
            .OnTable("PurchaseOrderLines").InSchema("inbound");
        Delete.Index("IX_PurchaseOrderLines_Po_DisplayOrder")
            .OnTable("PurchaseOrderLines").InSchema("inbound");
        Delete.Column("DisplayOrder").FromTable("PurchaseOrderLines").InSchema("inbound");
        Delete.Column("LineNo").FromTable("PurchaseOrderLines").InSchema("inbound");
    }
}
