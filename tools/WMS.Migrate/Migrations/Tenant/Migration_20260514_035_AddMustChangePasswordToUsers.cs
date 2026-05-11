using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 27 — adds security.Users.MustChangePassword. Tenant
// onboarding creates a bootstrap ADMIN user with a temp password +
// MustChangePassword=true; first-login interceptor redirects to
// /Account/ChangePassword and refuses to proceed until cleared.
//
// Default 0 for existing rows — Phase 25's password change clears the
// flag explicitly, but legacy users keep it false (they've been
// signing in fine without a forced change).
//
// Idempotent COL_LENGTH guard.
[Migration(20260514035L)]
[Tags("Tenant")]
public class Migration_20260514_035_AddMustChangePasswordToUsers : MigrationBase
{
    public override void Up() =>
        Execute.Sql(@"
IF COL_LENGTH('security.Users', 'MustChangePassword') IS NULL
    ALTER TABLE [security].[Users]
    ADD MustChangePassword BIT NOT NULL CONSTRAINT DF_Users_MustChangePassword DEFAULT 0;
");

    public override void Down() =>
        Execute.Sql(@"
IF OBJECT_ID('DF_Users_MustChangePassword', 'D') IS NOT NULL
    ALTER TABLE [security].[Users] DROP CONSTRAINT DF_Users_MustChangePassword;
IF COL_LENGTH('security.Users', 'MustChangePassword') IS NOT NULL
    ALTER TABLE [security].[Users] DROP COLUMN MustChangePassword;
");
}
