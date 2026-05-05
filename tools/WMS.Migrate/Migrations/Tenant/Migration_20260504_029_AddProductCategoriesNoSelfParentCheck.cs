using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Defensive guard against self-parenting (ParentId = Id) on the
// ProductCategories tree. Cheap CHECK, no perf impact on writes.
//
// Does NOT catch longer cycles (A→B→A) — those are handled in the BLL
// for now; a follow-up chunk may add a trigger if cycle bugs surface.
[Migration(20260504029L)]
[Tags("Tenant")]
public class Migration_20260504_029_AddProductCategoriesNoSelfParentCheck : MigrationBase
{
    // Idempotent CHECK ADD: re-running the migration after a partial
    // failure stays safe. FluentMigrator has no fluent CHECK API; raw SQL
    // with sys.check_constraints existence test is the standard escape
    // hatch.
    public override void Up() =>
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.check_constraints " +
            "WHERE name = 'CK_ProductCategories_NoSelfParent' " +
            "AND parent_object_id = OBJECT_ID('master.ProductCategories')) " +
            "BEGIN " +
            "ALTER TABLE [master].[ProductCategories] " +
            "ADD CONSTRAINT CK_ProductCategories_NoSelfParent " +
            "CHECK (ParentId IS NULL OR ParentId <> Id); " +
            "END");

    public override void Down() =>
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.check_constraints " +
            "WHERE name = 'CK_ProductCategories_NoSelfParent' " +
            "AND parent_object_id = OBJECT_ID('master.ProductCategories')) " +
            "ALTER TABLE [master].[ProductCategories] " +
            "DROP CONSTRAINT CK_ProductCategories_NoSelfParent;");
}
