namespace WMS.Web.Services.Mappers;

// Phase 12 — PascalCase ↔ lowercase for CycleCount.Status. DB CHECK
// enforces ('Counting' | 'Review' | 'Applied' | 'Cancelled').
public static class CycleCountStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Counting"  => "counting",
        "Review"    => "review",
        "Applied"   => "applied",
        "Cancelled" => "cancelled",
        _           => "counting",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "counting"          => "Counting",
        "review"            => "Review",
        "applied"           => "Applied",
        "cancelled"         => "Cancelled",
        _                   => null,
    };

    public static string ToBadgeVariant(string db) => db switch
    {
        "Counting"  => "warning",
        "Review"    => "info",
        "Applied"   => "success",
        "Cancelled" => "neutral",
        _           => "neutral",
    };
}
