namespace WMS.Web.Services.Mappers;

// PascalCase ↔ lowercase for ReceivingHeader.Status. Same shape as
// PurchaseOrderStatusMapper (Phase 9A); DB CHECK enforces the
// closed set ('Draft' | 'Posted' | 'Cancelled').
public static class ReceivingStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Draft"     => "draft",
        "Posted"    => "posted",
        "Cancelled" => "cancelled",
        _           => "posted",  // defensive
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "draft"             => "Draft",
        "posted"            => "Posted",
        "cancelled"         => "Cancelled",
        _                   => null,
    };
}
