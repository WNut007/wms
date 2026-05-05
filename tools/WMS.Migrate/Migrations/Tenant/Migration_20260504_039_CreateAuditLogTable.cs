using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504039L)]
[Tags("Tenant")]
public class Migration_20260504_039_CreateAuditLogTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("AuditLog").InSchema("security")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // NULL UserId is intentional — system-generated events
            // (background jobs, anonymous login attempts) have no actor.
            // NO ACTION on delete: audit history must outlive its
            // referenced user. BLL nulls the FK explicitly if a user is
            // hard-deleted; soft-delete (IsActive = 0) is the normal path
            // and leaves the FK intact.
            .WithColumn("UserId").AsGuid().Nullable()
                .ForeignKey("FK_AuditLog_Users",
                            "security", "Users", "Id")
                .OnDelete(System.Data.Rule.None)

            // Free-form event vocabulary (Login / Logout / PermissionGrant
            // / PermissionRevoke / UserCreated / UserLocked / etc.). No
            // CHECK so application can introduce new event types without
            // a schema change.
            .WithColumn("EventType").AsAnsiString(50).NotNullable()

            // Optional target — many events reference a domain entity
            // (e.g. EventType='UserUpdated' + EntityType='User' +
            // EntityId=<user-uuid>). Both nullable for events without a
            // single target (e.g. Login).
            .WithColumn("EntityType").AsAnsiString(50).Nullable()
            .WithColumn("EntityId").AsGuid().Nullable()

            // Network context. VARCHAR(45) fits both IPv4 dotted notation
            // and the longest IPv6 form including embedded IPv4.
            .WithColumn("IpAddress").AsAnsiString(45).Nullable()
            .WithColumn("UserAgent").AsAnsiString(500).Nullable()

            // JSON payload of event-specific details. NVARCHAR(MAX) so
            // unicode (Thai user input, etc.) survives the round trip.
            .WithColumn("Details").AsCustom("NVARCHAR(MAX)").Nullable()

            // Audit-of-audit is intentionally absent. AuditLog rows are
            // immutable: no UpdatedAt / CreatedBy / UpdatedBy columns,
            // because the row IS the record of what happened. Mirrors the
            // intentional skips applied to master.SystemAuditLog and
            // master.LoginAttempts in TD-001.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"));

        // Time-windowed user activity ("show user X's events for the
        // last 7 days"). CreatedAt DESC so the newest events sort first.
        Create.Index("IX_AuditLog_User_Time")
            .OnTable("AuditLog").InSchema("security")
            .OnColumn("UserId").Ascending()
            .OnColumn("CreatedAt").Descending();

        // EventType-led scan ("how many failed-login events today?").
        Create.Index("IX_AuditLog_Event_Time")
            .OnTable("AuditLog").InSchema("security")
            .OnColumn("EventType").Ascending()
            .OnColumn("CreatedAt").Descending();

        // Entity-history lookup ("what changed on this Role / User?").
        // Filtered to skip rows with no entity target.
        Execute.Sql(
            "CREATE INDEX IX_AuditLog_Entity " +
            "ON [security].[AuditLog] (EntityType, EntityId, CreatedAt DESC) " +
            "WHERE EntityType IS NOT NULL AND EntityId IS NOT NULL;");
    }

    public override void Down()
    {
        // Explicit DROP INDEX mirrors the explicit CREATE INDEX in Up; DROP TABLE
        // would also drop the index, but symmetry keeps the rollback obvious.
        Execute.Sql("DROP INDEX IX_AuditLog_Entity ON [security].[AuditLog];");
        Delete.Table("AuditLog").InSchema("security");
    }
}
