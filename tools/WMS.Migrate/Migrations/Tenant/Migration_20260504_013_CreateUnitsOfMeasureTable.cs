using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504013L)]
[Tags("Tenant")]
public class Migration_20260504_013_CreateUnitsOfMeasureTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("UnitsOfMeasure").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            // Tenant-wide unique code — UoMs are shared across all warehouses
            // (e.g. 'EA', 'PACK', 'CASE', 'KG', 'L').
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(50).NotNullable()

            // NotNullable + CHECK: Type drives conversion validity
            // (cannot convert Weight→Volume) and unit picker grouping.
            .WithColumn("Type").AsAnsiString(20).NotNullable()

            // Base UoM within a Type (e.g. EA for Count, KG for Weight). One
            // base per Type is expected; not enforced at DB — BLL validates
            // before insert/update so deprecation flips don't trigger DB errors.
            .WithColumn("IsBase").AsBoolean().NotNullable().WithDefaultValue(0)

            // IsActive added (not in spec) for consistency with other master
            // tables — allows soft-deprecation when a UoM is retired.
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)

            // Audit fields per standardized pattern (CreatedAt/UpdatedAt/
            // CreatedBy/UpdatedBy). No Version column.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Active-UoM lookup for picker dropdowns.
        Create.Index("IX_UnitsOfMeasure_Active")
            .OnTable("UnitsOfMeasure").InSchema("master")
            .OnColumn("IsActive").Ascending()
            .OnColumn("Code").Ascending();

        // Type-filtered active lookup (e.g. "list all active weight units").
        Create.Index("IX_UnitsOfMeasure_Type")
            .OnTable("UnitsOfMeasure").InSchema("master")
            .OnColumn("Type").Ascending()
            .OnColumn("IsActive").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[UnitsOfMeasure] " +
            "ADD CONSTRAINT CK_UnitsOfMeasure_Type " +
            "CHECK (Type IN ('Count', 'Weight', 'Volume', 'Length'));");
    }

    public override void Down() =>
        Delete.Table("UnitsOfMeasure").InSchema("master");
}
