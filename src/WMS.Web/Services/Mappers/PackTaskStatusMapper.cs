namespace WMS.Web.Services.Mappers;

// Phase 14D — PascalCase ↔ lowercase for PackTask.Status. DB CHECK
// enforces ('Pending' | 'Packed' | 'Cancelled') — simpler than Pick's
// 5-state machine because pack workflow is single-shot for MVP (no
// InProgress / Save Progress).
public static class PackTaskStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Pending"   => "pending",
        "Packed"    => "packed",
        "Cancelled" => "cancelled",
        _           => "pending",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "pending"           => "Pending",
        "packed"            => "Packed",
        "cancelled"         => "Cancelled",
        _                   => null,
    };

    public static string ToBadgeVariant(string db) => db switch
    {
        "Pending"   => "neutral",
        "Packed"    => "success",
        "Cancelled" => "neutral",
        _           => "neutral",
    };
}
