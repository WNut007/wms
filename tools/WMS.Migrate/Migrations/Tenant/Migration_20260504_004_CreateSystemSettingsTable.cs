using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

[Migration(20260504004L)]
[Tags("Tenant")]
public class Migration_20260504_004_CreateSystemSettingsTable : MigrationBase
{
    public override void Up()
    {
        // Key/value config table — natural key on [Key], no surrogate Id.
        Create.Table("SystemSettings").InSchema("master")
            .WithColumn("Key").AsAnsiString(100).PrimaryKey()
            .WithColumn("Value").AsCustom("NVARCHAR(MAX)").Nullable()
            .WithColumn("Category").AsAnsiString(50).Nullable()
            .WithColumn("DataType").AsAnsiString(20).NotNullable().WithDefaultValue("string")
            .WithColumn("Description").AsString(500).Nullable()
            .WithColumn("IsSystemManaged").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // Lookup by category (e.g., load all 'Operations' settings on app start).
        Create.Index("IX_Settings_Category").OnTable("SystemSettings").InSchema("master")
            .OnColumn("Category").Ascending()
            .OnColumn("Key").Ascending();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [master].[SystemSettings] " +
            "ADD CONSTRAINT CK_SystemSettings_DataType " +
            "CHECK (DataType IN ('string', 'int', 'decimal', 'bool', 'json'));");
    }

    public override void Down() =>
        Delete.Table("SystemSettings").InSchema("master");
}
