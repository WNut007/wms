using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Phase 17 (ADR-009) — outbound.PackVideos. Pack-specific metadata
// for a video that lives in documents.Files (storage substrate
// reused from Phase 5).
//
// Lifecycle:
//   1. Operator clicks "Record" on Pack Detail submit form
//   2. Browser MediaRecorder captures WebM blob
//   3. POST /PackTasks/UploadVideo/{id} writes the bytes via
//      IDocumentStorageService → documents.Files row + on-disk file
//   4. Service inserts outbound.PackVideos row pointing at the
//      DocumentFileId
//   5. Hangfire PackVideoRetentionCleanupJob (daily 03:00 UTC,
//      configurable retention days) deletes both rows + on-disk
//      bytes once RecordedAt < NOW - RetentionDays
//
// One-to-many with PackTask (operator can record multiple takes;
// each Upload POST inserts a new row). Per-task playback in the UI
// surfaces the latest by RecordedAt — older rows are still in
// documents.Files and visible in the admin browser if needed.
//
// CASCADE on PackTaskId — if a pack task is somehow hard-deleted
// (currently never happens; cancellation just flips status), its
// videos go too. The documents.Files row uses NO ACTION (consistent
// with Phase 5 audit-trail intent — files outlive their entity refs).
[Migration(20260510032L)]
[Tags("Tenant")]
public class Migration_20260510_032_CreatePackVideosTable : MigrationBase
{
    public override void Up()
    {
        Create.Table("PackVideos").InSchema("outbound")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            .WithColumn("PackTaskId").AsGuid().NotNullable()
                .ForeignKey("FK_PackVideos_PackTask",
                            "outbound", "PackTasks", "Id")
                .OnDelete(System.Data.Rule.Cascade)

            // FK to documents.Files — the actual blob lives there.
            // NO ACTION on delete: PackVideos row deletion (via the
            // retention job) deletes the documents.Files row first,
            // so this FK never blocks; the constraint exists to
            // catch bugs that try to delete files out from under
            // referencing PackVideos rows.
            .WithColumn("DocumentFileId").AsGuid().NotNullable()
                .ForeignKey("FK_PackVideos_DocumentFile",
                            "documents", "Files", "Id")

            // Video duration in whole seconds — captured client-side
            // before upload (MediaRecorder gives us the elapsed time).
            // Useful for UI playback estimate without parsing the
            // file. NOT NULL because every recording has a duration.
            .WithColumn("DurationSec").AsInt32().NotNullable()

            // Wall-clock when the recording was made (browser-side
            // when Stop was clicked). Server-side UploadedAt on the
            // documents.Files row would also work but RecordedAt is
            // load-bearing for the retention job's WHERE clause.
            .WithColumn("RecordedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("RecordedBy").AsGuid().Nullable()
                .ForeignKey("FK_PackVideos_RecordedBy", "security", "Users", "Id")

            // Standard audit (no Version — recordings are appended,
            // never edited; same convention as PackTaskLines + Cartons).
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("CreatedBy").AsGuid().Nullable()
                .ForeignKey("FK_PackVideos_CreatedBy", "security", "Users", "Id");

        // Per-task playback lookup — UI fetches the latest video
        // for a pack task by RecordedAt DESC.
        Create.Index("IX_PackVideos_PackTask")
            .OnTable("PackVideos").InSchema("outbound")
            .OnColumn("PackTaskId").Ascending()
            .OnColumn("RecordedAt").Descending();

        // Retention job's WHERE clause: WHERE RecordedAt < cutoff.
        // Single-column index suffices since the job doesn't filter
        // by anything else.
        Create.Index("IX_PackVideos_RecordedAt")
            .OnTable("PackVideos").InSchema("outbound")
            .OnColumn("RecordedAt").Ascending();

        Execute.Sql(
            "ALTER TABLE [outbound].[PackVideos] " +
            "ADD CONSTRAINT CK_PackVideos_DurationSec_NonNegative " +
            "CHECK (DurationSec >= 0);");
    }

    public override void Down()
    {
        Delete.Table("PackVideos").InSchema("outbound");
    }
}
