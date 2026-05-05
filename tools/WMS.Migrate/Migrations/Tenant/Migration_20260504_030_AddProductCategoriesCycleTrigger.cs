using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Defense-in-depth complement to CK_ProductCategories_NoSelfParent (029).
// The CHECK catches the trivial case (ParentId = Id); this trigger walks
// the full parent chain and rejects any cycle of arbitrary length.
//
// AFTER INSERT, UPDATE — runs after the row mutation lands, then
// ROLLBACK on cycle detection. INSTEAD OF would require manually
// re-implementing INSERT/UPDATE; AFTER + ROLLBACK is the standard
// pattern for post-write validation.
//
// Walk strategy: a recursive CTE seeded from the `inserted` rows climbs
// ParentId one level at a time, stopping at NULL parent, the safety
// depth cap (50), or as soon as a row would loop on itself. A cycle
// is detected when (RowId, AncestorId) ever produces a pair where
// AncestorId equals the original RowId.
[Migration(20260504030L)]
[Tags("Tenant")]
public class Migration_20260504_030_AddProductCategoriesCycleTrigger : MigrationBase
{
    // CREATE OR ALTER (SQL Server 2016 SP1+) keeps this re-runnable —
    // re-applying after a partial failure simply replaces the trigger
    // body. FluentMigrator has no fluent trigger API; raw SQL is the
    // standard escape hatch.
    public override void Up() =>
        Execute.Sql(@"
CREATE OR ALTER TRIGGER [master].[trg_ProductCategories_NoCycles]
ON [master].[ProductCategories]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Cheap exit when ParentId column wasn't part of the change.
    IF NOT UPDATE(ParentId) RETURN;

    -- No work when none of the affected rows have a parent (root inserts).
    IF NOT EXISTS (SELECT 1 FROM inserted WHERE ParentId IS NOT NULL) RETURN;

    -- A CTE cannot live directly inside IF EXISTS / a subquery in T-SQL,
    -- so the cycle check is staged through a flag. SELECT ... = 1 only
    -- writes to @hasCycle when the CTE produces a matching row, leaving
    -- the initial 0 untouched in the no-cycle case.
    DECLARE @hasCycle BIT = 0;

    -- Walk parent chain per affected row. Recursion stops at NULL parent,
    -- safety depth cap (50), or when we'd loop on a cycle.
    ;WITH Ancestors AS (
        SELECT i.Id AS RowId, i.ParentId AS AncestorId, 1 AS Depth
        FROM inserted i
        WHERE i.ParentId IS NOT NULL

        UNION ALL

        SELECT a.RowId, p.ParentId, a.Depth + 1
        FROM Ancestors a
        JOIN [master].[ProductCategories] p ON p.Id = a.AncestorId
        WHERE p.ParentId IS NOT NULL
          AND a.Depth < 50
          AND a.AncestorId <> a.RowId
    )
    SELECT TOP 1 @hasCycle = 1 FROM Ancestors WHERE AncestorId = RowId;

    IF @hasCycle = 1
    BEGIN
        RAISERROR('ProductCategories: parent chain forms a cycle.', 16, 1);
        ROLLBACK TRANSACTION;
    END
END;
");

    public override void Down() =>
        Execute.Sql(
            "DROP TRIGGER IF EXISTS [master].[trg_ProductCategories_NoCycles];");
}
