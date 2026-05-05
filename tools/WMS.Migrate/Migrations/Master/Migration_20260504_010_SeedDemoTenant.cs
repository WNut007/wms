using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

// Seeds the bootstrap tenant ('DEMO') and maps the bootstrap admin user
// to it so login Step 1 (AuthService.AuthenticateAsync) can resolve the
// admin's tenants on a fresh install.
//
// DatabaseName = WMS_Tenant_Template — the same DB that
// Migration_20260504_041_SeedAdminUser populates with security.Users.
// Real per-tenant DBs come later via tenant onboarding; until then the
// template DB doubles as the demo tenant's working DB.
//
// Idempotent: tenant existence-checked by Code, mapping by
// (UserEmail, Code) — re-runs after a partial failure are safe.
[Migration(20260504010L)]
[Tags("Master")]
public class Migration_20260504_010_SeedDemoTenant : MigrationBase
{
    private const string TenantCode = "DEMO";
    private const string TenantName = "Demo Tenant";
    private const string DatabaseName = "WMS_Tenant_Template";
    private const string AdminEmail = "nwuthipongworachoke@gmail.com";

    public override void Up()
    {
        // 1) Insert the DEMO tenant. Idempotent by Code (unique).
        Execute.Sql(
            $"IF NOT EXISTS (SELECT 1 FROM [master].[Tenants] WHERE Code = '{TenantCode}') " +
            $"INSERT INTO [master].[Tenants] " +
            $"(Id, Code, Name, DatabaseName, Status, CreatedAt) " +
            $"VALUES (NEWID(), '{TenantCode}', N'{TenantName}', " +
            $"'{DatabaseName}', 'Active', SYSUTCDATETIME());");

        // 2) Map admin → DEMO with IsDefault = 1 so AuthenticateAsync
        // finds it as the user's primary tenant. Lookup-and-insert
        // pattern resolves the tenant Id by Code.
        Execute.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM [master].[UserTenantMap] m
    JOIN [master].[Tenants] t ON t.Id = m.TenantId
    WHERE m.UserEmail = '" + AdminEmail + @"' AND t.Code = '" + TenantCode + @"'
)
INSERT INTO [master].[UserTenantMap] (Id, UserEmail, TenantId, IsDefault, CreatedAt)
SELECT NEWID(), '" + AdminEmail + @"', t.Id, 1, SYSUTCDATETIME()
FROM [master].[Tenants] t WHERE t.Code = '" + TenantCode + @"';");
    }

    public override void Down()
    {
        // Reverse order — mapping first, then tenant. The FK on
        // UserTenantMap.TenantId is ON DELETE CASCADE, so deleting the
        // tenant alone would also clear the row, but doing it explicitly
        // is clearer.
        Execute.Sql(@"
DELETE m
FROM [master].[UserTenantMap] m
JOIN [master].[Tenants] t ON t.Id = m.TenantId
WHERE m.UserEmail = '" + AdminEmail + @"' AND t.Code = '" + TenantCode + @"';");

        Execute.Sql($"DELETE FROM [master].[Tenants] WHERE Code = '{TenantCode}';");
    }
}
