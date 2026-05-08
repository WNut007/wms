using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// documents.Files — metadata-only table; the bytes live on disk under
// Storage.Local.RootPath / {tenantId} / {entityType} / {entityId} / {fileId}{ext}.
// Splitting metadata from bytes lets us swap LocalFileStorageService for
// an Azure Blob impl later without re-wiring callers (ADR for Phase 5
// pending — this matches the abstraction shape from Phase 4).
//
// EntityType + EntityId are loosely-typed string FKs (no DB constraint)
// so a single Files row can attach to a Product, Warehouse, Customer, or
// future entity without per-table joins. The composite index covers the
// list-by-entity read which is the hot path.
//
// Schema creation must be its own batch (CREATE SCHEMA cannot share a
// statement) — wrapped in EXEC the same way the inbound + security
// schema migrations do it.
[Migration(20260508001L)]
[Tags("Tenant")]
public class Migration_20260508_001_CreateDocumentsSchemaAndFilesTable : MigrationBase
{
    public override void Up()
    {
        Execute.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'documents') " +
            "EXEC('CREATE SCHEMA [documents]');");

        Create.Table("Files").InSchema("documents")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewGuid)

            // Loose entity reference. EntityType is the domain-level name
            // ('Product' / 'Warehouse' / 'Customer' / ...). EntityId is
            // the business key for that entity (SKU, Code, ...) — kept
            // string-typed because not all entities key by GUID.
            .WithColumn("EntityType").AsAnsiString(50).NotNullable()
            .WithColumn("EntityId").AsString(100).NotNullable()

            // The original filename the user uploaded — preserved for
            // download Content-Disposition. The on-disk filename is
            // {Id}{Ext} so renames + collisions are impossible.
            .WithColumn("FileName").AsString(260).NotNullable()
            .WithColumn("ContentType").AsAnsiString(120).NotNullable()
            .WithColumn("FileSize").AsInt64().NotNullable()

            // Lower-cased extension including the leading dot — matches
            // DocumentStorageOptions.AllowedExtensions for cheap re-check
            // at download time.
            .WithColumn("Extension").AsAnsiString(20).NotNullable()

            // Free-form bucket the UI uses to colour-code rows
            // (Specification / Manual / Pricing / Certificate / Contract / ...).
            // Defaults to 'Document' so we never need a NULL branch in views.
            .WithColumn("Category").AsAnsiString(50).NotNullable()
                .WithDefaultValue("Document")

            // Resolved path relative to Storage.Local.RootPath. Stored so
            // a future move to a different RootPath only changes the prefix.
            // Format: {tenantId}/{entityType}/{entityId}/{fileId}{ext}
            .WithColumn("StorageProvider").AsAnsiString(20).NotNullable()
                .WithDefaultValue("Local")
            .WithColumn("StorageKey").AsString(500).NotNullable()

            // Soft-delete flag. Hard-delete still removes the disk bytes
            // and the row — IsArchived is reserved for "hide from list
            // but keep on disk for audit" (Phase 5+).
            .WithColumn("IsArchived").AsBoolean().NotNullable().WithDefaultValue(false)

            // Audit + optimistic concurrency. UploadedBy / UploadedAt are
            // aliases for CreatedBy / CreatedAt to match the API vocabulary
            // — single source of truth lives in the audit columns.
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
                .WithDefaultValue(RawSql.Insert("SYSUTCDATETIME()"))
            .WithColumn("UpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CreatedBy").AsGuid().Nullable()
            .WithColumn("UpdatedBy").AsGuid().Nullable()
            .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);

        // Hot read: "list documents for entity X" — the Detail page
        // panel and the API List endpoint both run this filter on every
        // page load. Filtered to skip archived rows so the index is
        // dense for the common case.
        Execute.Sql(
            "CREATE INDEX IX_Files_Entity " +
            "ON [documents].[Files] (EntityType, EntityId, CreatedAt DESC) " +
            "WHERE IsArchived = 0;");

        // Wire CreatedBy / UpdatedBy → security.Users(Id) following the
        // tenant audit-FK pattern from migration 045. NO ACTION on delete:
        // a user with upload history must be soft-deleted (IsActive = 0),
        // not hard-deleted, so the audit trail stays intact.
        Execute.Sql(@"
ALTER TABLE [documents].[Files]
    ADD CONSTRAINT FK_Files_CreatedBy
    FOREIGN KEY (CreatedBy) REFERENCES [security].[Users](Id)
    ON DELETE NO ACTION;");

        Execute.Sql(@"
ALTER TABLE [documents].[Files]
    ADD CONSTRAINT FK_Files_UpdatedBy
    FOREIGN KEY (UpdatedBy) REFERENCES [security].[Users](Id)
    ON DELETE NO ACTION;");
    }

    public override void Down()
    {
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Files_UpdatedBy') " +
            "ALTER TABLE [documents].[Files] DROP CONSTRAINT FK_Files_UpdatedBy;");
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Files_CreatedBy') " +
            "ALTER TABLE [documents].[Files] DROP CONSTRAINT FK_Files_CreatedBy;");
        Execute.Sql("DROP INDEX IF EXISTS IX_Files_Entity ON [documents].[Files];");
        Delete.Table("Files").InSchema("documents");
        Execute.Sql(
            "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'documents') " +
            "DROP SCHEMA [documents];");
    }
}
