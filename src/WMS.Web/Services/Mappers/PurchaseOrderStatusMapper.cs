namespace WMS.Web.Services.Mappers;

// PascalCase ↔ lowercase translation between inbound.PurchaseOrders
// .Status (DB-canonical, satisfies CK_PurchaseOrders_Status) and the
// wire format /PurchaseOrders front-end speaks. Same Phase 6B pattern
// as ProductStatusMapper / CustomerStatusMapper.
//
// DB CHECK constraint is the authority on legal Status values; this
// mapper just renders / parses the same closed set on the wire.
// Unknown wire values fall through to "no filter applied", same UX
// as user choosing "all".
public static class PurchaseOrderStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Open"      => "open",
        "Receiving" => "receiving",
        "Closed"    => "closed",
        "Cancelled" => "cancelled",
        // Defensive — should never hit if DB CHECK is doing its job.
        _           => "open",
    };

    // null / "all" / empty / unknown → null (= no filter applied).
    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "open"              => "Open",
        "receiving"         => "Receiving",
        "closed"            => "Closed",
        "cancelled"         => "Cancelled",
        _                   => null,
    };
}
