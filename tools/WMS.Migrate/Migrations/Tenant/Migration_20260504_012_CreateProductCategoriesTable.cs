using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504012L)]
[Tags("Tenant")]
public class Migration_20260504_012_CreateProductCategoriesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("ProductCategories").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Tenant-wide unique code — categories are shared across warehouses
            // and feed Product master.
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(100).NotNullable()

            // Self-FK for hierarchy. NO ACTION (default) on delete — prevents
            // accidental subtree drop; BLL must reparent or soft-delete children
            // before removing a parent.
            .WithColumn("ParentId").AsGuid().Nullable()
                .ForeignKey("FK_ProductCategories_Parent",
                            "master", "ProductCategories", "Id")
                .OnDelete(System.Data.Rule.None)

            // Denormalized hierarchy path (e.g. '/Electronics/Mobile/Smartphones').
            // Maintained by BLL on insert/move; supports fast prefix-match queries
            // (LIKE '/Electronics/%'). No DB trigger — Phase 2 may add one.
            // TODO(TD-003): Path is VARCHAR but Name is NVARCHAR — unicode (Thai)
            // category names are lossy when the path trigger concats them. Plan:
            // ALTER to NVARCHAR(500).
            .WithColumn("Path").AsAnsiString(500).Nullable()

            .WithColumn("Description").AsString(500).Nullable()
            // Sort order within a parent. Nullable — UI can fall back to Name sort.
            .WithColumn("DisplayOrder").AsInt32().Nullable()

            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields — written explicitly (not via AddAuditFields helper) so
            // CreatedAt keeps the SYSUTCDATETIME() default. No Version column:
            // optimistic concurrency not needed for low-churn category metadata.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Children-of-parent lookup, ordered by DisplayOrder for tree rendering.
        Create.Index("IX_Categories_Parent")
            .OnTable("ProductCategories").InSchema("master")
            .OnColumn("ParentId").Ascending()
            .OnColumn("DisplayOrder").Ascending();

        // Path prefix-match queries (LIKE '/Electronics/%') — sargable on
        // leading-anchored patterns.
        Create.Index("IX_Categories_Path")
            .OnTable("ProductCategories").InSchema("master")
            .OnColumn("Path").Ascending();
    }

    public override void Down() =>
        Delete.Table("ProductCategories").InSchema("master");
}
