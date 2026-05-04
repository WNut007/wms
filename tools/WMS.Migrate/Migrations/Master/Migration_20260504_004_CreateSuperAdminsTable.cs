using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

[Migration(20260504004L)]
[Tags("Master")]
public class Migration_20260504_004_CreateSuperAdminsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("SuperAdmins").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("Email").AsAnsiString(100).NotNullable().Unique()
            .WithColumn("PasswordHash").AsAnsiString(500).NotNullable()
            .WithColumn("Permissions").AsCustom("NVARCHAR(MAX)").Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("LastLoginAt").AsDateTime2().Nullable();

        // FluentMigrator has no fluent filtered-index API; raw SQL is the standard escape hatch.
        Execute.Sql(
            "CREATE INDEX IX_SuperAdmins_Active " +
            "ON [master].[SuperAdmins] (IsActive) " +
            "WHERE IsActive = 1;");
    }

    public override void Down()
    {
        // Explicit DROP INDEX mirrors the explicit CREATE INDEX in Up; DROP TABLE
        // would also drop the index, but symmetry keeps the rollback obvious.
        Execute.Sql("DROP INDEX IX_SuperAdmins_Active ON [master].[SuperAdmins];");
        Delete.Table("SuperAdmins").InSchema("master");
    }
}
