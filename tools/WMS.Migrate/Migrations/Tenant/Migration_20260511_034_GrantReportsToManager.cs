using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 23 — grants REPORTS.VIEW to the MANAGER role with View flag.
// ADMIN gets it automatically via the BLL bypass (per the Migration_043
// header convention — functions added after 043 don't need explicit
// ADMIN grants).
//
// Picker / Packer left without Reports — operational roles don't need
// dashboard surfaces. Adding them later is a one-line addition to this
// or a new migration.
//
// Idempotent + defensive Down() (same shape as Migration_044).
[Migration(20260511034L)]
[Tags("Tenant")]
public class Migration_20260511_034_GrantReportsToManager : MigrationBase
{
    public override void Up() =>
        Execute.Sql(@"
INSERT INTO [security].[RoleFunctionPermissions]
    (Id, RoleId, FunctionId, CanView, CanAdd, CanEdit, CanDelete, CanApprove, CreatedAt)
SELECT NEWID(), r.Id, f.Id, 1, 0, 0, 0, 0, SYSUTCDATETIME()
FROM [security].[Roles] r
CROSS JOIN [security].[Functions] f
WHERE r.Code = 'MANAGER' AND f.Code = 'REPORTS.VIEW'
  AND NOT EXISTS (
      SELECT 1 FROM [security].[RoleFunctionPermissions] rfp
      WHERE rfp.RoleId = r.Id AND rfp.FunctionId = f.Id
  );");

    public override void Down() =>
        Execute.Sql(@"
DELETE rfp
FROM [security].[RoleFunctionPermissions] rfp
JOIN [security].[Roles] r     ON r.Id = rfp.RoleId
JOIN [security].[Functions] f ON f.Id = rfp.FunctionId
WHERE r.Code = 'MANAGER' AND f.Code = 'REPORTS.VIEW';");
}
