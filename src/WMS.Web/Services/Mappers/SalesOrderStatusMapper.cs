namespace WMS.Web.Services.Mappers;

// Phase 14A — PascalCase ↔ lowercase for SalesOrder.Status. DB CHECK
// enforces the closed set ('Draft' | 'Open' | 'Cancelled') for MVP;
// 14B widens this with allocation/pick/pack states via ALTER.
public static class SalesOrderStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Draft"     => "draft",
        "Open"      => "open",
        "Cancelled" => "cancelled",
        _           => "draft",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "draft"             => "Draft",
        "open"              => "Open",
        "cancelled"         => "Cancelled",
        _                   => null,
    };

    public static string ToBadgeVariant(string db) => db switch
    {
        "Draft"     => "neutral",
        "Open"      => "success",
        "Cancelled" => "neutral",
        _           => "neutral",
    };
}
