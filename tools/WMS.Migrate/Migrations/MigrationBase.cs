using FluentMigrator;
using FluentMigrator.Builders.Create.Table;

namespace WMS.Migrate.Migrations;

public abstract class MigrationBase : Migration
{
    // Adds CreatedAt/UpdatedAt/CreatedBy/UpdatedBy/Version columns mirroring
    // WMS.Domain.Entities.BaseEntity. Returns the same builder so the caller
    // can keep chaining .WithColumn(...) for table-specific columns.
    protected static ICreateTableColumnOptionOrWithColumnSyntax AddAuditFields(
        ICreateTableWithColumnSyntax t) =>
        t.WithColumn("CreatedAt").AsDateTime2().NotNullable()
         .WithColumn("UpdatedAt").AsDateTime2().Nullable()
         .WithColumn("CreatedBy").AsGuid().Nullable()
         .WithColumn("UpdatedBy").AsGuid().Nullable()
         .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

    // Shorthand for a single-column ascending index named IX_{table}_{column}.
    protected void CreateIndex(string schema, string table, string column) =>
        Create.Index($"IX_{table}_{column}")
              .OnTable(table).InSchema(schema)
              .OnColumn(column).Ascending();
}
