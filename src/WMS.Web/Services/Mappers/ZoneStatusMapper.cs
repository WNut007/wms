namespace WMS.Web.Services.Mappers;

// bool ↔ "active"/"inactive" wire translation for master.Zones
// .IsActive. Same shape as BoxType/UoM/Warehouse status mappers.
//
// Zone.Type (Receiving/Storage/Picking/etc.) is a separate concept —
// CHECK-constrained 8-state enum — and passes through as the wire
// string verbatim. NormaliseType lives on the controller.
public static class ZoneStatusMapper
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
