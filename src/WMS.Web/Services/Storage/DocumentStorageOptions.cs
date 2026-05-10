namespace WMS.Web.Services.Storage;

// Bound from "Storage" section of appsettings.json. Validated lightly at
// startup (Provider must be one of the known values; RootPath required when
// Provider == Local). Intentionally kept simple — Phase 5 only ships Local;
// Azure / S3 join via the same options shape later.
public sealed class DocumentStorageOptions
{
    public const string SectionName = "Storage";

    // "Mock" keeps the in-memory store from Phase 4 (useful for tests
    // that don't want to touch disk). "Local" is the Phase 5 default.
    public string Provider { get; set; } = "Local";

    public LocalOptions Local { get; set; } = new();

    // Hard cap on a single upload, enforced before the file is written
    // to disk. Phase 17 raised from 25 → 50 MB so a 60-second 720p
    // WebM pack-video (~30 MB typical) fits. _DocumentsPanel dropzone
    // copy still says 25 MB — pack videos go through their own upload
    // path, not the generic doc dropzone.
    public int MaxFileSizeMB { get; set; } = 50;

    // Lower-cased extensions including the leading dot. Whitelisted to keep
    // executables out — "everything else" is an expensive default.
    // Phase 17 added .webm + .mp4 for pack-video uploads.
    public string[] AllowedExtensions { get; set; } = new[]
    {
        ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".csv", ".txt",
        ".png", ".jpg", ".jpeg", ".webp", ".gif",
        ".webm", ".mp4",
    };

    public sealed class LocalOptions
    {
        // Absolute or relative path. Relative paths resolve against the
        // ContentRoot at startup so working-directory drift between IIS,
        // Kestrel, and dotnet test doesn't move files.
        public string RootPath { get; set; } = "App_Data/storage";
    }
}
