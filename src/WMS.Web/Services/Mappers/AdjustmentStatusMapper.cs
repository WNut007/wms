namespace WMS.Web.Services.Mappers;

// PascalCase ↔ lowercase for Adjustment.Status. DB CHECK enforces
// the closed set ('Pending' | 'Applied' | 'Rejected').
public static class AdjustmentStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Pending"  => "pending",
        "Applied"  => "applied",
        "Rejected" => "rejected",
        _          => "pending",  // defensive
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "pending"           => "Pending",
        "applied"           => "Applied",
        "rejected"          => "Rejected",
        _                   => null,
    };

    // Helper for the wmsd-badge class.
    public static string ToBadgeVariant(string db) => db switch
    {
        "Pending"  => "warning",
        "Applied"  => "success",
        "Rejected" => "neutral",
        _          => "neutral",
    };
}
