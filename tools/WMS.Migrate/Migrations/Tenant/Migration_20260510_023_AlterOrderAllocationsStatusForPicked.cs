using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14C — widen CK_OrderAllocations_Status to add 'Picked' state.
// Updated audit-status invariant: Picked branch requires PickedAt +
// PickedBy populated, and ReleasedAt remains NULL (Picked and Released
// are distinct terminal states — Picked = consumed by a pick task,
// Released = cancel-reversal).
//
// Adds PickedBy + PickedAt audit columns mirroring AllocatedBy/At and
// ReleasedBy/At pattern.
[Migration(20260510023L)]
[Tags("Tenant")]
public class Migration_20260510_023_AlterOrderAllocationsStatusForPicked : MigrationBase
{
    public override void Up()
    {
        // Add Picked-state audit pair.
        Alter.Table("OrderAllocations").InSchema("outbound")
            .AddColumn("PickedAt").AsDateTime2().Nullable()
            .AddColumn("PickedBy").AsGuid().Nullable()
                .ForeignKey("FK_OrderAllocations_PickedBy", "security", "Users", "Id");

        // Drop and re-add Status CHECK.
        Execute.Sql(
            "ALTER TABLE [outbound].[OrderAllocations] " +
            "DROP CONSTRAINT CK_OrderAllocations_Status;");

        Execute.Sql(
            "ALTER TABLE [outbound].[OrderAllocations] " +
            "ADD CONSTRAINT CK_OrderAllocations_Status " +
            "CHECK (Status IN ('Active', 'Released', 'Picked'));");

        // Drop and re-add audit invariant — now 3 branches.
        Execute.Sql(
            "ALTER TABLE [outbound].[OrderAllocations] " +
            "DROP CONSTRAINT CK_OrderAllocations_AuditMatchesStatus;");

        Execute.Sql(@"
ALTER TABLE [outbound].[OrderAllocations]
ADD CONSTRAINT CK_OrderAllocations_AuditMatchesStatus
CHECK (
    (Status = 'Active'   AND ReleasedAt IS NULL     AND ReleasedBy IS NULL AND PickedAt IS NULL AND PickedBy IS NULL)
 OR (Status = 'Released' AND ReleasedAt IS NOT NULL AND PickedAt IS NULL)
 OR (Status = 'Picked'   AND PickedAt   IS NOT NULL AND ReleasedAt IS NULL)
);");
    }

    public override void Down()
    {
        Execute.Sql(
            "ALTER TABLE [outbound].[OrderAllocations] " +
            "DROP CONSTRAINT CK_OrderAllocations_AuditMatchesStatus;");

        Execute.Sql(@"
ALTER TABLE [outbound].[OrderAllocations]
ADD CONSTRAINT CK_OrderAllocations_AuditMatchesStatus
CHECK (
    (Status = 'Active'   AND ReleasedAt IS NULL     AND ReleasedBy IS NULL)
 OR (Status = 'Released' AND ReleasedAt IS NOT NULL)
);");

        Execute.Sql(
            "ALTER TABLE [outbound].[OrderAllocations] " +
            "DROP CONSTRAINT CK_OrderAllocations_Status;");

        Execute.Sql(
            "ALTER TABLE [outbound].[OrderAllocations] " +
            "ADD CONSTRAINT CK_OrderAllocations_Status " +
            "CHECK (Status IN ('Active', 'Released'));");

        Delete.Column("PickedBy").FromTable("OrderAllocations").InSchema("outbound");
        Delete.Column("PickedAt").FromTable("OrderAllocations").InSchema("outbound");
    }
}
