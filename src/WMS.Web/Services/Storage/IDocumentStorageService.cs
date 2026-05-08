namespace WMS.Web.Services.Storage;

public interface IDocumentStorageService
{
    Task<DocumentMetadata> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string entityType,
        string entityId,
        string category,
        string uploadedBy,
        CancellationToken ct = default);

    Task<DocumentMetadata?> GetMetadataAsync(Guid documentId, CancellationToken ct = default);

    // Opens a stream over the stored bytes plus returns the metadata so
    // the controller can stamp Content-Disposition / Content-Type without
    // a second round trip. Caller owns the stream — Mock returns
    // MemoryStream, Local returns FileStream. Returns null when the
    // metadata row exists but the bytes have gone missing on disk.
    Task<(Stream Stream, DocumentMetadata Metadata)?> GetStreamAsync(
        Guid documentId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid documentId, CancellationToken ct = default);

    Task<List<DocumentMetadata>> ListByEntityAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default);
}

public class DocumentMetadata
{
    public Guid DocumentId { get; init; } = Guid.NewGuid();
    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "";
    public long FileSize { get; init; }
    public string Category { get; init; } = "Document";
    public string StorageProvider { get; init; } = "mock";
    public string StorageKey { get; init; } = "";
    public string UploadedBy { get; init; } = "";
    public DateTime UploadedAt { get; init; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }

    public string FileSizeFormatted
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            return $"{FileSize / (1024.0 * 1024.0):F1} MB";
        }
    }

    public string IconClass => GetIconForContentType(ContentType, FileName);
    public string IconColorBg => GetIconBgForContentType(ContentType, FileName);
    public string IconColorFg => GetIconFgForContentType(ContentType, FileName);

    private static string GetIconForContentType(string contentType, string fileName)
    {
        if (contentType.StartsWith("image/")) return "ti-photo";
        if (contentType == "application/pdf") return "ti-file-type-pdf";
        if (fileName.EndsWith(".xlsx") || fileName.EndsWith(".xls")) return "ti-file-spreadsheet";
        if (fileName.EndsWith(".docx") || fileName.EndsWith(".doc")) return "ti-file-type-doc";
        if (fileName.EndsWith(".csv")) return "ti-file-spreadsheet";
        return "ti-file";
    }

    private static string GetIconBgForContentType(string contentType, string fileName)
    {
        if (contentType == "application/pdf") return "#FCEBEB";
        if (contentType.StartsWith("image/")) return "#EEEDFE";
        if (contentType.Contains("spreadsheet") || contentType.Contains("excel")
            || fileName.EndsWith(".xlsx") || fileName.EndsWith(".xls") || fileName.EndsWith(".csv"))
            return "#EAF3DE";
        if (contentType.Contains("word") || contentType.Contains("document")
            || fileName.EndsWith(".docx") || fileName.EndsWith(".doc"))
            return "#E6F1FB";
        return "#F1EFE8";
    }

    private static string GetIconFgForContentType(string contentType, string fileName)
    {
        if (contentType == "application/pdf") return "#A32D2D";
        if (contentType.StartsWith("image/")) return "#534AB7";
        if (contentType.Contains("spreadsheet") || contentType.Contains("excel")
            || fileName.EndsWith(".xlsx") || fileName.EndsWith(".xls") || fileName.EndsWith(".csv"))
            return "#27500A";
        if (contentType.Contains("word") || contentType.Contains("document")
            || fileName.EndsWith(".docx") || fileName.EndsWith(".doc"))
            return "#0C447C";
        return "#5F5E5A";
    }
}

// Thrown by UploadAsync when an upload is rejected before any bytes hit
// disk — extension not in the allowlist, file too large, etc. Surfaced
// by DocumentsController as 400 Bad Request.
public sealed class StorageValidationException : Exception
{
    public StorageValidationException(string message) : base(message) { }
}
