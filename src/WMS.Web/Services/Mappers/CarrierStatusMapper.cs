namespace WMS.Web.Services.Mappers;

// 4-state Status enum ↔ wire translation for master.Carriers.Status.
// Distinct from BoxType/UoM/Warehouse status mappers which translate
// bool ↔ "active"/"inactive" — Carriers uses a workflow lifecycle.
//
// Wire format is PascalCase here (matches the DB CHECK set exactly),
// not lowercase — keeps the JSON envelope easy to debug and avoids a
// cosmetic translation that buys nothing.
public static class CarrierStatusMapper
{
    public static readonly IReadOnlyList<string> All =
        new[] { "Inactive", "Configured", "Tested", "Production" };

    public static string ToWire(string status) => status;

    // null / "all" / empty / unknown → null (= no filter applied).
    // Known values pass through. Unknown silently drops the filter.
    public static string? FromWire(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire)) return null;
        if (wire.Equals("all", StringComparison.OrdinalIgnoreCase)) return null;
        return All.Contains(wire) ? wire : null;
    }

    // Wire form for the chip CSS variant (.wms-badge-{variant}).
    public static string Variant(string status) => status switch
    {
        "Inactive"   => "neutral",
        "Configured" => "info",
        "Tested"     => "warning",
        "Production" => "success",
        _            => "neutral",
    };
}
