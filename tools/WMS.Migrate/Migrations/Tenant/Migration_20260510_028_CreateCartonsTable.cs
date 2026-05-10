using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 14D — outbound.Cartons. Physical packaging per pack task.
//
// MVP simplification: ONE carton per pack task (UNIQUE constraint on
// PackTaskId enforces it). The eventual N-cartons-per-task path drops
// this UNIQUE in a future migration and adds a CartonContents many-to-
// many to record per-line per-carton splitting.
//
// Carton row is created at PackTask.SubmitAsync time alongside the
// task header flip Pending → Packed (kept atomic inside the same TX).
// Pre-Submit Cancel skips Carton entirely — nothing to undo.
//
// CartonNumber: CTN-YYYYMMDD-NNNN (consistent with SO/PCK/etc).
// BoxTypeId: nullable FK to master.BoxTypes for MVP (operator picks
// from an active list; "(unspecified)" allowed).
// WeightKg: nullable for MVP — scale integration is deferred (TD).
//          Operator can type a weight manually if they want to record it.
[Migration(20260510028L)]
[Tags("Tenant")]
public class Migration_20260510_028_CreateCartonsTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("Cartons").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Tenant-wide unique ID stamped server-side at submit.
            .WithColumn("CartonNumber").AsAnsiString(50).NotNullable().Unique()

            // 1:1 with PackTask for MVP. UNIQUE drops in a future
            // migration when multi-carton splitting lands.
            // CASCADE: cartons belong to the task; cancelled tasks could
            // be hard-deleted via this path (today only Pending tasks
            // can cancel — and no cartons exist yet at that point — but
            // the cascade is forward-stable).
            .WithColumn("PackTaskId").AsGuid().NotNullable()
                .ForeignKey("FK_Cartons_PackTask",
                            "outbound", "PackTasks", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            .WithColumn("BoxTypeId").AsGuid().Nullable()
                .ForeignKey("FK_Cartons_BoxType",
                            "master", "BoxTypes", "Id")

            // 3-decimal precision matches master.BoxTypes.EmptyWeightKg —
            // small parcels can shift carrier billing brackets at gram
            // resolution. Future: scale integration auto-populates.
            .WithColumn("WeightKg").AsDecimal(10, 3).Nullable()

            .WithColumn("Notes").AsString(500).Nullable()

            // Standard audit (no Version on cartons — appended once at
            // pack-submit time and never edited; if the operator needs
            // to change carton metadata, they'd cancel + regenerate).
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_Cartons_CreatedBy", "security", "Users", "Id")
            .WithColumn("UpdatedBy").AsGuid().Nullable()
                .ForeignKey("FK_Cartons_UpdatedBy", "security", "Users", "Id");

        // Enforces 1:1 PackTask → Carton for MVP. Drop this in a
        // future migration to enable multi-carton splitting.
        Create.Index("UX_Cartons_PackTask")
            .OnTable("Cartons").InSchema("outbound")
            .WithOptions().Unique()
            .OnColumn("PackTaskId").Ascending();

        Execute.Sql(
            "ALTER TABLE [outbound].[Cartons] " +
            "ADD CONSTRAINT CK_Cartons_WeightKg_NonNegative " +
            "CHECK (WeightKg IS NULL OR WeightKg >= 0);");
    }

    public override void Down()
    {
        Delete.Table("Cartons").InSchema("outbound");
    }
}
