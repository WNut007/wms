using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

// P0 #4 (post-Phase-30A fix) — in-flow forced password change.
//
// Adds `RequiresPasswordChange BIT NOT NULL DEFAULT 0` to
// master.PreAuthTokens so the token can carry "this user must change
// their password before login completes". Step 1 of the login flow
// (POST /Auth/Login email+password) sets this when User.MustChangePassword
// is true; the in-flow change-password step verifies the token + flag,
// applies the new password, then continues the normal 3-step chain.
//
// This closes the P0 bypass where a forced-change user landed on
// /Account/ChangePassword inside _OfficeLayout with a working sidebar —
// they could navigate away even though every protected request bounced
// back. By NOT issuing the session cookie until the password is changed,
// the sidebar simply doesn't render — no bypass surface.
//
// Idempotent — IF COL_LENGTH guard so re-runs are safe.
[Migration(20260518012L)]
[Tags("Master")]
public class Migration_20260518_012_ExtendPreAuthTokensForForcedPwChange : MigrationBase
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('master.PreAuthTokens', 'RequiresPasswordChange') IS NULL
    ALTER TABLE [master].[PreAuthTokens]
    ADD RequiresPasswordChange BIT NOT NULL
        CONSTRAINT DF_PreAuthTokens_RequiresPasswordChange DEFAULT 0;
");
    }

    public override void Down()
    {
        Execute.Sql(@"
IF OBJECT_ID('DF_PreAuthTokens_RequiresPasswordChange', 'D') IS NOT NULL
    ALTER TABLE [master].[PreAuthTokens] DROP CONSTRAINT DF_PreAuthTokens_RequiresPasswordChange;

IF COL_LENGTH('master.PreAuthTokens', 'RequiresPasswordChange') IS NOT NULL
    ALTER TABLE [master].[PreAuthTokens] DROP COLUMN RequiresPasswordChange;
");
    }
}
