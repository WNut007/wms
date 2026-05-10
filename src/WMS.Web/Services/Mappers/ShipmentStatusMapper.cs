namespace WMS.Web.Services.Mappers;

// Phase 14E — PascalCase ↔ lowercase for Shipment.Status. DB CHECK
// enforces ('Pending' | 'Shipped' | 'Cancelled') — same 3-state
// minimalism as 14D Pack.
public static class ShipmentStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Pending"   => "pending",
        "Shipped"   => "shipped",
        "Cancelled" => "cancelled",
        _           => "pending",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "pending"           => "Pending",
        "shipped"           => "Shipped",
        "cancelled"         => "Cancelled",
        _                   => null,
    };

    public static string ToBadgeVariant(string db) => db switch
    {
        "Pending"   => "neutral",
        "Shipped"   => "success",
        "Cancelled" => "neutral",
        _           => "neutral",
    };
}
