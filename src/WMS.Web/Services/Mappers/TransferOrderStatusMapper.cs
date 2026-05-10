namespace WMS.Web.Services.Mappers;

// Phase 13 (ADR-012) — PascalCase ↔ lowercase for TransferOrder.Status.
// DB CHECK enforces the closed set
// ('Draft' | 'Submitted' | 'Approved' | 'InTransit' | 'Received' |
//  'Cancelled' | 'Lost').
public static class TransferOrderStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Draft"     => "draft",
        "Submitted" => "submitted",
        "Approved"  => "approved",
        "InTransit" => "intransit",
        "Received"  => "received",
        "Cancelled" => "cancelled",
        "Lost"      => "lost",
        _           => "draft",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "draft"             => "Draft",
        "submitted"         => "Submitted",
        "approved"          => "Approved",
        "intransit"         => "InTransit",
        "received"          => "Received",
        "cancelled"         => "Cancelled",
        "lost"              => "Lost",
        _                   => null,
    };

    public static string ToBadgeVariant(string db) => db switch
    {
        "Draft"     => "neutral",
        "Submitted" => "info",
        "Approved"  => "info",
        "InTransit" => "warning",
        "Received"  => "success",
        "Cancelled" => "neutral",
        "Lost"      => "danger",
        _           => "neutral",
    };
}
