using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 10B (TD-023) — extends inbound.ReceivingHeaders with the audit
// trio that records WHO cancelled a posted receipt, WHEN, and WHY. All
// three columns are nullable because:
//   * Existing rows (Posted) have nothing to backfill.
//   * Future Posted / Draft rows leave them NULL until cancellation.
// Status='Cancelled' was already in CK_ReceivingHeaders_Status from
// migration 057 — the column flips to 'Cancelled' AND the audit trio
// populates atomically inside CancelReceivingAsync's TransactionScope.
//
// CancelReason: NVARCHAR(500) — long enough for an operator to write
// a useful explanation ("stock damaged on inspection", "vendor sent
// wrong SKU"); short enough to fit on the GRN reprint footer if we
// ever surface it there. Audit trail value is the central reason this
// column is required (per TD-023 design Q1).
[Migration(20260510008L)]
[Tags("Tenant")]
public class Migration_20260510_008_AddReceivingHeadersCancellationAudit : MigrationBase
{
    public override void Up()
    {
        Alter.Table("ReceivingHeaders").InSchema("inbound")
            .AddColumn("CancelledBy").AsGuid().Nullable()
                .ForeignKey("FK_ReceivingHeaders_CancelledBy",
                            "security", "Users", "Id")
            .AddColumn("CancelledAt").AsDateTime2().Nullable()
            .AddColumn("CancelReason").AsString(500).Nullable();
    }

    public override void Down()
    {
        Delete.ForeignKey("FK_ReceivingHeaders_CancelledBy")
              .OnTable("ReceivingHeaders").InSchema("inbound");

        Delete.Column("CancelledBy")
              .Column("CancelledAt")
              .Column("CancelReason")
              .FromTable("ReceivingHeaders").InSchema("inbound");
    }
}
