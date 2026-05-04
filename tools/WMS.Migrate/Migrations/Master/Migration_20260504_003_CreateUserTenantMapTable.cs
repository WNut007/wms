using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

[Migration(20260504003L)]
[Tags("Master")]
public class Migration_20260504_003_CreateUserTenantMapTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("UserTenantMap").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("UserEmail").AsAnsiString(100).NotNullable()
            .WithColumn("TenantId").AsGuid().NotNullable()
                .ForeignKey("FK_UserTenantMap_Tenants", "master", "Tenants", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Role").AsAnsiString(20).Nullable()
            .WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"));

        // Unique index doubles as the (UserEmail, TenantId) uniqueness constraint
        // and the primary lookup path — one physical structure instead of two.
        Create.Index("IX_UserTenant").OnTable("UserTenantMap").InSchema("master")
            .WithOptions().Unique()
            .OnColumn("UserEmail").Ascending()
            .OnColumn("TenantId").Ascending();

        Create.Index("IX_Tenant_Users").OnTable("UserTenantMap").InSchema("master")
            .OnColumn("TenantId").Ascending()
            .OnColumn("UserEmail").Ascending();
    }

    public override void Down() =>
        Delete.Table("UserTenantMap").InSchema("master");
}
