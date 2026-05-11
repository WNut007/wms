using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 23 — adds REPORTS.VIEW to the permissionable-function catalogue.
// Reports is a top-level module in SidebarMenuViewComponent.ModuleOrder
// (already wired with icon ti-chart-bar); seeding a REPORTS.* function
// surfaces the module to users who have permission on it.
//
// Single function for MVP. Per-report permission split (REPORTS.INVENTORY
// / REPORTS.ORDERS / REPORTS.KPIS) is a TD — bundle once enterprise
// tenants ask for it.
//
// Idempotent: existence-checked by Code, so re-runs add zero rows.
[Migration(20260511033L)]
[Tags("Tenant")]
public class Migration_20260511_033_SeedReportsFunction : MigrationBase
{
    public override void Up() =>
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [security].[Functions] WHERE Code = 'REPORTS.VIEW')
INSERT INTO [security].[Functions]
    (Id, Code, Name, Module, DisplayOrder, IsActive, CreatedAt)
VALUES
    (NEWID(), 'REPORTS.VIEW', N'Reports', 'Reports', 1, 1, SYSUTCDATETIME());");

    public override void Down() =>
        // CASCADE on RoleFunctionPermissions handles dependent rows.
        Execute.Sql("DELETE FROM [security].[Functions] WHERE Code = 'REPORTS.VIEW';");
}
