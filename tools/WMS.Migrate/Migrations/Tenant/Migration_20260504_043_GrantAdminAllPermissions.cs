using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Initial permission grant: ADMIN role × every Function seeded by 042,
// all five flags (View/Add/Edit/Delete/Approve) set. Runs as a single
// CROSS JOIN insert with NOT EXISTS so re-applying the migration
// produces zero new rows once the grants are in place.
//
// This is a bootstrap, not the long-term mechanism: the BLL is
// expected to special-case the ADMIN role to bypass the permission
// matrix entirely. Functions added by later migrations therefore
// don't need to back-fill ADMIN grants — the bypass takes care of it
// at runtime. The matrix rows still exist as a defensive layer and
// for permission introspection / reporting.
[Migration(20260504043L)]
[Tags("Tenant")]
public class Migration_20260504_043_GrantAdminAllPermissions : MigrationBase
{
    public override void Up() =>
        Execute.Sql(@"
INSERT INTO [security].[RoleFunctionPermissions]
    (Id, RoleId, FunctionId, CanView, CanAdd, CanEdit, CanDelete, CanApprove, CreatedAt)
SELECT NEWID(), r.Id, f.Id, 1, 1, 1, 1, 1, SYSUTCDATETIME()
FROM [security].[Roles] r
CROSS JOIN [security].[Functions] f
WHERE r.Code = 'ADMIN'
  AND NOT EXISTS (
      SELECT 1 FROM [security].[RoleFunctionPermissions] rfp
      WHERE rfp.RoleId = r.Id AND rfp.FunctionId = f.Id
  );");

    public override void Down() =>
        // Strip ADMIN's permission rows. CASCADE on the FK makes this
        // redundant if 042 is rolled back ahead of us, but keeping it
        // explicit means Down() works cleanly when only this migration
        // is reverted.
        Execute.Sql(@"
DELETE rfp
FROM [security].[RoleFunctionPermissions] rfp
JOIN [security].[Roles] r ON r.Id = rfp.RoleId
WHERE r.Code = 'ADMIN';");
}
