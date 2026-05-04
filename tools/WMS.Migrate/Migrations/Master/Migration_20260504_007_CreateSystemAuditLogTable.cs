using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

[Migration(20260504007L)]
[Tags("Master")]
public class Migration_20260504_007_CreateSystemAuditLogTable : MigrationBase
{
    public override void Up()
    {
        // No foreign keys: audit rows must remain readable even after the
        // referenced Tenant/User/Entity rows are deleted (immutability).
        Create.Table("SystemAuditLog").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("EventType").AsAnsiString(50).NotNullable()
            .WithColumn("Severity").AsAnsiString(20).NotNullable().WithDefaultValue("Info")
            .WithColumn("UserId").AsGuid().Nullable()
            .WithColumn("UserEmail").AsAnsiString(100).Nullable()
            .WithColumn("TenantId").AsGuid().Nullable()
            .WithColumn("EntityType").AsAnsiString(50).Nullable()
            .WithColumn("EntityId").AsGuid().Nullable()
            .WithColumn("Details").AsCustom("NVARCHAR(MAX)").Nullable()
            .WithColumn("IpAddress").AsAnsiString(45).Nullable()
            .WithColumn("Timestamp").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"));

        // Recent-events feed (admin dashboard).
        Create.Index("IX_SystemAudit_Timestamp").OnTable("SystemAuditLog").InSchema("master")
            .OnColumn("Timestamp").Descending();

        // Filter by event type, ordered newest-first.
        Create.Index("IX_SystemAudit_EventType").OnTable("SystemAuditLog").InSchema("master")
            .OnColumn("EventType").Ascending()
            .OnColumn("Timestamp").Descending();

        // Per-tenant audit trail.
        Create.Index("IX_SystemAudit_Tenant").OnTable("SystemAuditLog").InSchema("master")
            .OnColumn("TenantId").Ascending()
            .OnColumn("Timestamp").Descending();
    }

    public override void Down() =>
        Delete.Table("SystemAuditLog").InSchema("master");
}
