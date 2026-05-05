using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// inventory.Pallets — system-wide-unique pallet identifiers used to
// group stock physically.
//
// FK CreatedBy / UpdatedBy → security.Users is wired in migration 051
// alongside Lots and Stock.
[Migration(20260504049L)]
[Tags("Tenant")]
public class Migration_20260504_049_CreatePalletsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Pallets").InSchema("inventory")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Pallet number is tenant-wide unique — printed on the LPN
            // label and scanned across every flow.
            .WithColumn("PalletNumber").AsAnsiString(50).NotNullable().Unique()

            // Active = in use; Empty = no stock currently associated;
            // Damaged / Retired = excluded from new putaway. CHECK below.
            .WithColumn("Status").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Active")

            // Audit fields per standardized pattern.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                // SystemMethods.CurrentUTCDateTime maps to GETUTCDATE() (DATETIME precision);
                // spec requires SYSUTCDATETIME() (DATETIME2 precision).
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable();

        // FluentMigrator has no fluent CHECK constraint; raw SQL is the standard escape hatch.
        Execute.Sql(
            "ALTER TABLE [inventory].[Pallets] " +
            "ADD CONSTRAINT CK_Pallets_Status " +
            "CHECK (Status IN ('Active', 'Empty', 'Damaged', 'Retired'));");
    }

    public override void Down() =>
        Delete.Table("Pallets").InSchema("inventory");
}
