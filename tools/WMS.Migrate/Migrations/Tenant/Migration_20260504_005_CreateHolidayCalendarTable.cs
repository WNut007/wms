using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504005L)]
[Tags("Tenant")]
public class Migration_20260504_005_CreateHolidayCalendarTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("HolidayCalendar").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)
            .WithColumn("Date").AsDate().NotNullable()
            .WithColumn("Name").AsString(100).NotNullable()
            .WithColumn("Type").AsAnsiString(20).NotNullable()
            // NULL WarehouseId = global holiday (applies to all warehouses).
            // Default ON DELETE NO ACTION — protects holiday history if a
            // warehouse is removed; admin must reassign or delete first.
            .WithColumn("WarehouseId").AsGuid().Nullable()
                .ForeignKey("FK_HolidayCalendar_Warehouses", "master", "Warehouses", "Id")
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("Notes").AsString(500).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"));

        // One entry per (Date, WarehouseId). SQL Server treats NULL as a
        // single distinct value here, so at most one global (NULL) holiday
        // per date — which is the intended semantics.
        Create.Index("UX_HolidayCalendar_Date_Warehouse")
            .OnTable("HolidayCalendar").InSchema("master")
            .WithOptions().Unique()
            .OnColumn("Date").Ascending()
            .OnColumn("WarehouseId").Ascending();

        // Primary lookup path: "what holidays fall on/around date X?"
        Create.Index("IX_Holiday_Date").OnTable("HolidayCalendar").InSchema("master")
            .OnColumn("Date").Ascending()
            .OnColumn("IsActive").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[HolidayCalendar] " +
            "ADD CONSTRAINT CK_HolidayCalendar_Type " +
            "CHECK (Type IN ('Public', 'Company', 'Regional'));");
    }

    public override void Down() =>
        Delete.Table("HolidayCalendar").InSchema("master");
}
