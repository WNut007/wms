using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

[Migration(20260504005L)]
[Tags("Master")]
public class Migration_20260504_005_CreateLoginAttemptsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("LoginAttempts").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("Email").AsAnsiString(100).NotNullable()
            .WithColumn("IpAddress").AsAnsiString(45).Nullable()
            .WithColumn("UserAgent").AsString(500).Nullable()
            .WithColumn("Success").AsBoolean().NotNullable()
            .WithColumn("FailureReason").AsAnsiString(100).Nullable()
            .WithColumn("AttemptedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"));

        // Primary lookup path for per-email rate limiting: latest attempts first.
        Create.Index("IX_LoginAttempts_Email").OnTable("LoginAttempts").InSchema("master")
            .OnColumn("Email").Ascending()
            .OnColumn("AttemptedAt").Descending();

        // Per-IP rate limiting (block bursts from a single source).
        Create.Index("IX_LoginAttempts_Ip").OnTable("LoginAttempts").InSchema("master")
            .OnColumn("IpAddress").Ascending()
            .OnColumn("AttemptedAt").Descending();
    }

    public override void Down() =>
        Delete.Table("LoginAttempts").InSchema("master");
}
