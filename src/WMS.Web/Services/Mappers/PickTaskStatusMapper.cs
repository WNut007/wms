namespace WMS.Web.Services.Mappers;

// Phase 14C — PascalCase ↔ lowercase for PickTask.Status. DB CHECK
// enforces ('Pending' | 'InProgress' | 'Picked' | 'PartiallyPicked' |
// 'Cancelled').
public static class PickTaskStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Pending"         => "pending",
        "InProgress"      => "inprogress",
        "Picked"          => "picked",
        "PartiallyPicked" => "partiallypicked",
        "Cancelled"       => "cancelled",
        _                 => "pending",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "pending"           => "Pending",
        "inprogress"        => "InProgress",
        "picked"            => "Picked",
        "partiallypicked"   => "PartiallyPicked",
        "cancelled"         => "Cancelled",
        _                   => null,
    };

    public static string ToBadgeVariant(string db) => db switch
    {
        "Pending"         => "neutral",
        "InProgress"      => "warning",
        "Picked"          => "success",
        "PartiallyPicked" => "warning",   // short — needs follow-up
        "Cancelled"       => "neutral",
        _                 => "neutral",
    };
}
