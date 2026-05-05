using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Closes TD-003: master.ProductCategories.Path was VARCHAR(500) but
// Name is NVARCHAR(100), so the path-maintenance trigger (031) was
// lossy-converting unicode (Thai) names when concatenating them
// into Path. Widen Path to NVARCHAR(500) to match.
//
// ALTER COLUMN converts in-place and is data-preserving going from
// VARCHAR → NVARCHAR (always wider). Down() goes the other way; on a
// populated DB it would lose any non-ASCII characters, but the table
// is empty in this rebuild so the rollback is safe.
[Migration(20260504032L)]
[Tags("Tenant")]
public class Migration_20260504_032_AlterCategoryPathToUnicode : MigrationBase
{
    // IX_Categories_Path depends on the Path column, so SQL Server blocks
    // ALTER COLUMN until the index is dropped. Drop → ALTER → recreate is
    // the standard pattern; the index is recreated identically to its
    // definition in migration 012.
    //
    // Idempotent: existence-checked by the column's current type via
    // sys.columns + sys.types. Re-running after a partial failure is a
    // no-op once the column is already NVARCHAR.
    public override void Up() =>
        Execute.Sql(@"
IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID('master.ProductCategories')
      AND c.name = 'Path'
      AND t.name = 'varchar'
)
BEGIN
    DROP INDEX [IX_Categories_Path] ON [master].[ProductCategories];

    ALTER TABLE [master].[ProductCategories]
    ALTER COLUMN [Path] NVARCHAR(500) NULL;

    CREATE INDEX [IX_Categories_Path]
    ON [master].[ProductCategories] ([Path] ASC);
END");

    public override void Down() =>
        Execute.Sql(@"
IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID('master.ProductCategories')
      AND c.name = 'Path'
      AND t.name = 'nvarchar'
)
BEGIN
    DROP INDEX [IX_Categories_Path] ON [master].[ProductCategories];

    ALTER TABLE [master].[ProductCategories]
    ALTER COLUMN [Path] VARCHAR(500) NULL;

    CREATE INDEX [IX_Categories_Path]
    ON [master].[ProductCategories] ([Path] ASC);
END");
}
