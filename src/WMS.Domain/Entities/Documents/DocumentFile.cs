namespace WMS.Domain.Entities.Documents;

// Maps to documents.Files. Bytes live on disk under
// Storage.Local.RootPath / StorageKey — this row is metadata only.
// EntityType + EntityId form a loose, string-typed reference so a single
// table covers Product / Warehouse / Customer / future entities without
// per-table joins.
public sealed class DocumentFile : BaseEntity
{
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSize { get; set; }
    public string Extension { get; set; } = "";
    public string Category { get; set; } = "Document";
    public string StorageProvider { get; set; } = "Local";
    public string StorageKey { get; set; } = "";
    public bool IsArchived { get; set; }
}
