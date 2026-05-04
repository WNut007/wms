using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

[Migration(20260504006L)]
[Tags("Master")]
public class Migration_20260504_006_CreatePreAuthTokensTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("PreAuthTokens").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("UserEmail").AsAnsiString(100).NotNullable()
            .WithColumn("Token").AsAnsiString(500).NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable()
            .WithColumn("UsedAt").AsDateTime2().Nullable()
            .WithColumn("IpAddress").AsAnsiString(45).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"));

        // Primary lookup: redeem-by-token on the post-login redirect.
        Create.Index("IX_PreAuthTokens_Token").OnTable("PreAuthTokens").InSchema("master")
            .OnColumn("Token").Ascending();

        // Background sweep for expired tokens.
        Create.Index("IX_PreAuthTokens_Cleanup").OnTable("PreAuthTokens").InSchema("master")
            .OnColumn("ExpiresAt").Ascending();
    }

    public override void Down() =>
        Delete.Table("PreAuthTokens").InSchema("master");
}
