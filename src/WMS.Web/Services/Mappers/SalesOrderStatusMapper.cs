namespace WMS.Web.Services.Mappers;

// Phase 14A — PascalCase ↔ lowercase for SalesOrder.Status.
// Phase 14B widened the DB CHECK to:
//   'Draft' | 'Open' | 'Allocating' | 'Allocated' | 'Cancelled'
// 14C/D will widen further with Picking/Picked/Packed/Shipped/Closed.
public static class SalesOrderStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Draft"      => "draft",
        "Open"       => "open",
        "Allocating" => "allocating",
        "Allocated"  => "allocated",
        "Cancelled"  => "cancelled",
        _            => "draft",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "draft"             => "Draft",
        "open"              => "Open",
        "allocating"        => "Allocating",
        "allocated"         => "Allocated",
        "cancelled"         => "Cancelled",
        _                   => null,
    };

    public static string ToBadgeVariant(string db) => db switch
    {
        "Draft"      => "neutral",
        "Open"       => "success",   // submitted, awaiting allocation
        "Allocating" => "warning",   // partial — needs more stock
        "Allocated"  => "info",      // fully allocated, ready for pick (14C)
        "Cancelled"  => "neutral",
        _            => "neutral",
    };
}
