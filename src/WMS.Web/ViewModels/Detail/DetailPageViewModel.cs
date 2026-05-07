namespace WMS.Web.ViewModels.Detail;

public class DetailPageViewModel
{
    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string IconClass { get; init; } = "ti-box";
    public string IconBgColor { get; init; } = "#E6F1FB";
    public string IconFgColor { get; init; } = "#0C447C";

    public string StatusLabel { get; init; } = "";
    public string StatusVariant { get; init; } = "success";

    public string BreadcrumbParent { get; init; } = "";
    public string BreadcrumbParentUrl { get; init; } = "";

    public List<StatCard> Stats { get; init; } = new();
    public bool ShowImagesTab { get; init; }
    public List<ImageItem> Images { get; init; } = new();
    public List<DocumentItem> Documents { get; init; } = new();
    public List<ActivityItem> Activities { get; init; } = new();
    public List<QuickAction> QuickActions { get; init; } = new();
    public List<KeyValuePair<string, string>> OverviewFields { get; init; } = new();
    public List<KeyValuePair<string, string>> Properties { get; init; } = new();
}

public record StatCard(string Label, string Value, string? AccentColor = null);

public record ImageItem(int Order, string Label, string Url, bool IsPrimary);

public record DocumentItem(
    Guid DocumentId,
    string FileName,
    string Category,
    string CategoryColorBg,
    string CategoryColorFg,
    string IconClass,
    string IconBgColor,
    string IconFgColor,
    string FileSizeFormatted,
    string UploadedBy,
    DateTime UploadedAt,
    string UploadedAtRelative);

public record ActivityItem(
    string Title,
    string Description,
    string IconClass,
    string IconColor,
    DateTime Timestamp,
    string TimestampRelative,
    string DateGroup,
    string? DiffOld = null,
    string? DiffNew = null);

public record QuickAction(string Label, string IconClass, string Url);
