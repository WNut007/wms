using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Hands ownership of ProductCategories.Path to the database. AFTER
// INSERT, UPDATE trigger that rebuilds Path for the affected row plus
// every descendant whenever ParentId or Name changes.
//
// Contract change vs migration 012: BLL must no longer set Path on
// insert/move. The trigger is the single source of truth from this
// migration onward.
//
// Known limitation: the trigger only fires when ParentId or Name is in
// the UPDATE SET list. A direct `UPDATE ... SET Path = ...` that
// doesn't touch ParentId or Name will leave the bad value alone — a
// strict guard would need an INSTEAD OF UPDATE trigger, deferred until
// it's actually needed.
//
// Trigger order: trg_ProductCategories_NoCycles (030) is pinned to
// fire FIRST, because the path trigger walks descendants via a
// recursive CTE and would loop forever on a cycle that hasn't been
// rejected yet.
[Migration(20260504031L)]
[Tags("Tenant")]
public class Migration_20260504_031_AddProductCategoriesPathTrigger : MigrationBase
{
    public override void Up()
    {
        // CREATE OR ALTER (SQL Server 2016 SP1+) keeps re-runs safe.
        // The early UPDATE(ParentId) OR UPDATE(Name) check is what
        // breaks the trigger's self-recursion: when this trigger does
        // its own UPDATE it only touches Path, so the next firing
        // exits immediately.
        Execute.Sql(@"
CREATE OR ALTER TRIGGER [master].[trg_ProductCategories_MaintainPath]
ON [master].[ProductCategories]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Skip when neither parent nor name moved. Also breaks the trigger's
    -- own re-entry when it UPDATEs Path (Path-only writes leave both
    -- UPDATE() probes FALSE).
    IF NOT (UPDATE(ParentId) OR UPDATE(Name)) RETURN;

    -- Subtree CTE: anchor on the affected rows joined to their (post-update)
    -- parents, then descend into children. NewPath is built bottom-up by
    -- concatenating each level's Name onto the running path.
    -- ISNULL(p.Path, '') handles root rows where the parent join misses
    -- (no parent → starts as '' so the result is '/RootName').
    -- CAST to NVARCHAR(MAX) keeps anchor and recursive parts type-compatible:
    -- CONCAT's implicit length grows on each recursion, which trips the
    -- recursive-CTE type-match rule without an explicit cast.
    ;WITH Subtree AS (
        SELECT
            c.Id,
            CAST(CONCAT(ISNULL(p.Path, ''), '/', c.Name) AS NVARCHAR(MAX)) AS NewPath
        FROM inserted i
        JOIN [master].[ProductCategories] c ON c.Id = i.Id
        LEFT JOIN [master].[ProductCategories] p ON p.Id = c.ParentId

        UNION ALL

        SELECT
            d.Id,
            CAST(CONCAT(s.NewPath, '/', d.Name) AS NVARCHAR(MAX))
        FROM Subtree s
        JOIN [master].[ProductCategories] d ON d.ParentId = s.Id
    )
    UPDATE c
    SET Path = s.NewPath
    FROM [master].[ProductCategories] c
    JOIN Subtree s ON s.Id = c.Id
    OPTION (MAXRECURSION 100);
END;
");

        // Pin the cycle trigger to fire first. Without this, SQL Server
        // is free to fire the path trigger first and walk a cyclic
        // subtree to MAXRECURSION before the cycle check ever runs.
        Execute.Sql(@"
EXEC sp_settriggerorder
    @triggername = 'master.trg_ProductCategories_NoCycles',
    @order = 'First',
    @stmttype = 'INSERT';
EXEC sp_settriggerorder
    @triggername = 'master.trg_ProductCategories_NoCycles',
    @order = 'First',
    @stmttype = 'UPDATE';");

        // One-time backfill so the table is in a consistent state from
        // the moment the trigger takes over. Empty table → no-op; rows
        // that already exist get their Path recomputed top-down. Path
        // trigger doesn't fire on this UPDATE because the SET list
        // touches Path only.
        // Same recursive-CTE type-match rule applies here — CAST keeps anchor
        // and recursive parts producing NVARCHAR(MAX) so SQL Server accepts
        // the UNION ALL.
        Execute.Sql(@"
;WITH Tree AS (
    SELECT Id, CAST(CONCAT('/', Name) AS NVARCHAR(MAX)) AS NewPath
    FROM [master].[ProductCategories]
    WHERE ParentId IS NULL

    UNION ALL

    SELECT c.Id, CAST(CONCAT(t.NewPath, '/', c.Name) AS NVARCHAR(MAX))
    FROM Tree t
    JOIN [master].[ProductCategories] c ON c.ParentId = t.Id
)
UPDATE c
SET Path = t.NewPath
FROM [master].[ProductCategories] c
JOIN Tree t ON t.Id = c.Id
OPTION (MAXRECURSION 100);");
    }

    public override void Down()
    {
        // Drop path trigger first; the cycle trigger order reset is
        // safe to do unconditionally afterward.
        Execute.Sql(
            "DROP TRIGGER IF EXISTS [master].[trg_ProductCategories_MaintainPath];");

        // Restore the cycle trigger to default (unordered) firing —
        // Up() set it to 'First', so Down() must undo. Guarded by
        // OBJECT_ID so it's a no-op if migration 030 has already been
        // rolled back ahead of us.
        Execute.Sql(@"
IF OBJECT_ID('master.trg_ProductCategories_NoCycles', 'TR') IS NOT NULL
BEGIN
    EXEC sp_settriggerorder
        @triggername = 'master.trg_ProductCategories_NoCycles',
        @order = 'None',
        @stmttype = 'INSERT';
    EXEC sp_settriggerorder
        @triggername = 'master.trg_ProductCategories_NoCycles',
        @order = 'None',
        @stmttype = 'UPDATE';
END");
    }
}
