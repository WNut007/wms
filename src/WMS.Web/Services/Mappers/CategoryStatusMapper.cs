namespace WMS.Web.Services.Mappers;

// bool ↔ "active"/"inactive" wire translation for
// master.ProductCategories.IsActive. Same shape as BoxType/UoM/Warehouse
// mappers — Categories have a bool soft-delete flag (no separate
// workflow status).
public static class CategoryStatusMapper
{
    public static string ToWire(bool isActive) =>
        isActive ? "active" : "inactive";

    public static bool? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "active"            => true,
        "inactive"          => false,
        _                   => null,
    };
}
