using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

[Migration(20260504002L)]
[Tags("Master")]
public class Migration_20260504_002_CreateTenantsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Tenants").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("DatabaseName").AsAnsiString(50).NotNullable()
            .WithColumn("ConnectionStringEncrypted").AsCustom("VARBINARY(MAX)").Nullable()
            .WithColumn("Status").AsAnsiString(20).NotNullable().WithDefaultValue("Active")
            .WithColumn("SubscriptionTier").AsAnsiString(20).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() which is
                // DATETIME-precision; spec requires SYSUTCDATETIME() (DATETIME2).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable();

        Create.Index("IX_Tenants_Status").OnTable("Tenants").InSchema("master")
            .OnColumn("Status").Ascending()
            .OnColumn("Code").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Tenants] " +
            "ADD CONSTRAINT CK_Tenants_Status " +
            "CHECK (Status IN ('Active', 'Suspended', 'Inactive'));");
    }

    public override void Down() =>
        Delete.Table("Tenants").InSchema("master");
}
