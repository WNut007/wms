namespace WMS.Web.Services.Mappers;

// bool ↔ "active"/"inactive" wire translation for master.UnitsOfMeasure
// .IsActive. Same shape as BoxTypeStatusMapper / WarehouseStatusMapper.
public static class UomStatusMapper
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
