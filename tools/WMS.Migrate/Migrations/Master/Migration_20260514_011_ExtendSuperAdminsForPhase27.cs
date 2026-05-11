using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

// Phase 27 — extend master.SuperAdmins with the columns Phase 25's
// lockout / password-change patterns expect. Original Phase 1 table
// (migration 004) shipped before the lockout invariants existed; adding
// the columns now keeps the existing FK on master audit fields stable.
//
// 4 columns added:
//   - FailedLoginAttempts INT NOT NULL DEFAULT 0
//   - LockedUntil DATETIME2 NULL
//   - FullName NVARCHAR(200) NULL  (display label; Email remains the natural key)
//   - MustChangePassword BIT NOT NULL DEFAULT 0
//
// Idempotent — IF COL_LENGTH(...) IS NULL guard so re-runs are safe.
[Migration(20260514011L)]
[Tags("Master")]
public class Migration_20260514_011_ExtendSuperAdminsForPhase27 : MigrationBase
{
    public override void Up()
    {
        Execute.Sql(@"
IF COL_LENGTH('master.SuperAdmins', 'FailedLoginAttempts') IS NULL
    ALTER TABLE [master].[SuperAdmins]
    ADD FailedLoginAttempts INT NOT NULL CONSTRAINT DF_SuperAdmins_FailedLogin DEFAULT 0;

IF COL_LENGTH('master.SuperAdmins', 'LockedUntil') IS NULL
    ALTER TABLE [master].[SuperAdmins]
    ADD LockedUntil DATETIME2 NULL;

IF COL_LENGTH('master.SuperAdmins', 'FullName') IS NULL
    ALTER TABLE [master].[SuperAdmins]
    ADD FullName NVARCHAR(200) NULL;

IF COL_LENGTH('master.SuperAdmins', 'MustChangePassword') IS NULL
    ALTER TABLE [master].[SuperAdmins]
    ADD MustChangePassword BIT NOT NULL CONSTRAINT DF_SuperAdmins_MustChangePassword DEFAULT 0;
");
    }

    public override void Down()
    {
        // Drop default constraints first (SQL Server requires explicit
        // DROP CONSTRAINT before DROP COLUMN when a DF_ is attached).
        Execute.Sql(@"
IF OBJECT_ID('DF_SuperAdmins_MustChangePassword', 'D') IS NOT NULL
    ALTER TABLE [master].[SuperAdmins] DROP CONSTRAINT DF_SuperAdmins_MustChangePassword;
IF OBJECT_ID('DF_SuperAdmins_FailedLogin', 'D') IS NOT NULL
    ALTER TABLE [master].[SuperAdmins] DROP CONSTRAINT DF_SuperAdmins_FailedLogin;

IF COL_LENGTH('master.SuperAdmins', 'MustChangePassword') IS NOT NULL
    ALTER TABLE [master].[SuperAdmins] DROP COLUMN MustChangePassword;
IF COL_LENGTH('master.SuperAdmins', 'FullName') IS NOT NULL
    ALTER TABLE [master].[SuperAdmins] DROP COLUMN FullName;
IF COL_LENGTH('master.SuperAdmins', 'LockedUntil') IS NOT NULL
    ALTER TABLE [master].[SuperAdmins] DROP COLUMN LockedUntil;
IF COL_LENGTH('master.SuperAdmins', 'FailedLoginAttempts') IS NOT NULL
    ALTER TABLE [master].[SuperAdmins] DROP COLUMN FailedLoginAttempts;
");
    }
}
