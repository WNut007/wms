using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Seeds the four canonical built-in roles every tenant DB starts with.
// IsSystemRole = 1 marks them as undeletable from the BLL — tenants
// can extend with custom roles, but Admin / Picker / Packer / Manager
// are guaranteed to be present.
//
// Idempotent: existence-checked by Code (the unique key), so re-running
// after a partial failure or after a row was already inserted manually
// is safe.
[Migration(20260504040L)]
[Tags("Tenant")]
public class Migration_20260504_040_SeedDefaultRoles : MigrationBase
{
    private static readonly (string Code, string Name, string Description)[] Roles =
    {
        ("ADMIN",   "Administrator", "Full system access; can manage users, roles, and configuration"),
        ("MANAGER", "Manager",       "Operational oversight; approves adjustments and reviews exceptions"),
        ("PICKER",  "Picker",        "Picks items from storage to fulfil pick tasks"),
        ("PACKER",  "Packer",        "Packs picked items into containers and verifies orders"),
    };

    public override void Up()
    {
        foreach (var (code, name, description) in Roles)
        {
            // N-prefix on Name / Description preserves unicode if a future
            // role description includes non-ASCII text. Code is ASCII so
            // plain quotes are fine.
            Execute.Sql(
                $"IF NOT EXISTS (SELECT 1 FROM [security].[Roles] WHERE Code = '{code}') " +
                $"INSERT INTO [security].[Roles] " +
                $"(Id, Code, Name, Description, IsSystemRole, IsActive, CreatedAt) " +
                $"VALUES (NEWID(), '{code}', N'{name}', N'{description}', 1, 1, SYSUTCDATETIME());");
        }
    }

    public override void Down() =>
        // CASCADE on RoleFunctionPermissions / UserRoles sweeps any rows
        // referencing these roles. Rollback intentionally tears the
        // built-in role slate down.
        Execute.Sql(
            "DELETE FROM [security].[Roles] " +
            "WHERE Code IN ('ADMIN', 'MANAGER', 'PICKER', 'PACKER');");
}
