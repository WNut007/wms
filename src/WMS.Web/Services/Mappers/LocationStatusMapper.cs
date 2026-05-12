namespace WMS.Web.Services.Mappers;

// 3-state Status enum ↔ wire translation for master.Locations.Status.
// Distinct from the bool↔wire mappers (BoxType/UoM/Warehouse) — Status
// here is an operational lifecycle (Active/Blocked/Maintenance), not a
// soft-delete flag. IsActive bool handled separately at the controller.
public static class LocationStatusMapper
{
    public static readonly IReadOnlyList<string> All =
        new[] { "Active", "Blocked", "Maintenance" };

    public static string ToWire(string status) => status;

    public static string? FromWire(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire)) return null;
        if (wire.Equals("all", StringComparison.OrdinalIgnoreCase)) return null;
        return All.Contains(wire) ? wire : null;
    }

    public static string Variant(string status) => status switch
    {
        "Active"      => "success",
        "Blocked"     => "danger",
        "Maintenance" => "warning",
        _             => "neutral",
    };
}
