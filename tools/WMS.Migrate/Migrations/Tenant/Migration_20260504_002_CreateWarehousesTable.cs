using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504002L)]
[Tags("Tenant")]
public class Migration_20260504_002_CreateWarehousesTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Warehouses").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("Code").AsAnsiString(20).NotNullable().Unique()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Address").AsString(500).Nullable()
            .WithColumn("Type").AsAnsiString(20).NotNullable().WithDefaultValue("Main")
            .WithColumn("PhoneNumber").AsAnsiString(20).Nullable()
            .WithColumn("ManagerName").AsString(100).Nullable()
            .WithColumn("TimeZoneId").AsAnsiString(50).Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Active-warehouse lookups by code (sidebar pickers, scan validation).
        Create.Index("IX_Warehouses_Active").OnTable("Warehouses").InSchema("master")
            .OnColumn("IsActive").Ascending()
            .OnColumn("Code").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[Warehouses] " +
            "ADD CONSTRAINT CK_Warehouses_Type " +
            "CHECK (Type IN ('Main', 'Satellite', 'Branch'));");
    }

    public override void Down() =>
        Delete.Table("Warehouses").InSchema("master");
}
