namespace WMS.Web.Services.Mappers;

// Phase 14A — PascalCase ↔ lowercase for SalesOrder.Status.
// Phase 14B widened the DB CHECK to:
//   'Draft' | 'Open' | 'Allocating' | 'Allocated' | 'Cancelled'
// Phase 14C added: Picking | Picked | PartiallyPicked.
// Phase 14D added: Packed.
// Phase 14E added: Shipped (terminal happy state).
public static class SalesOrderStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Draft"           => "draft",
        "Open"            => "open",
        "Allocating"      => "allocating",
        "Allocated"       => "allocated",
        "Picking"         => "picking",
        "Picked"          => "picked",
        "PartiallyPicked" => "partiallypicked",
        "Packed"          => "packed",
        "Shipped"         => "shipped",
        "Cancelled"       => "cancelled",
        _                 => "draft",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "draft"             => "Draft",
        "open"              => "Open",
        "allocating"        => "Allocating",
        "allocated"         => "Allocated",
        "picking"           => "Picking",
        "picked"            => "Picked",
        "partiallypicked"   => "PartiallyPicked",
        "packed"            => "Packed",
        "shipped"           => "Shipped",
        "cancelled"         => "Cancelled",
        _                   => null,
    };

    public static string ToBadgeVariant(string db) => db switch
    {
        "Draft"           => "neutral",
        "Open"            => "success",   // submitted, awaiting allocation
        "Allocating"      => "warning",   // partial — needs more stock
        "Allocated"       => "info",      // fully allocated, ready for pick
        "Picking"         => "warning",   // pick task generated, in flight
        "Picked"          => "success",   // pick task submitted, full pick
        "PartiallyPicked" => "warning",   // pick task submitted, short
        "Packed"          => "info",      // pack submitted, ready for ship
        "Shipped"         => "success",   // dispatched — terminal happy state
        "Cancelled"       => "neutral",
        _                 => "neutral",
    };
}
