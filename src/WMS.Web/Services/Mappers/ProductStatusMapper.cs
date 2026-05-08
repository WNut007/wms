namespace WMS.Web.Services.Mappers;

// PascalCase ↔ lowercase translation between master.Products.Status
// (DB-canonical, satisfies CK_Products_Status) and the wire format the
// existing /Products front-end speaks. Single source of truth — used
// by ProductsController on every list filter and JSON response.
//
// The DB CHECK constraint is the authority on legal Status values;
// this mapper just renders / parses the same closed set on the wire.
// Unknown wire values (incl. mock's 'out_of_stock', which was never
// a real schema status) are rejected by FromWire — they fall through
// to "no filter applied", same UX as the user choosing "all".
public static class ProductStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Active"       => "active",
        "Inactive"     => "inactive",
        "Discontinued" => "discontinued",
        "Draft"        => "draft",
        // Defensive — should never hit if the DB CHECK is doing its
        // job. Picks 'active' so the UI doesn't show a blank chip.
        _              => "active",
    };

    // null / "all" / empty / unknown → null (= no filter applied).
    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "active"            => "Active",
        "inactive"          => "Inactive",
        "discontinued"      => "Discontinued",
        "draft"             => "Draft",
        // Mock UI's 'out_of_stock' chip — not a schema state. Drop
        // silently (controller treats as no filter); future "low
        // stock" / "out of stock" derived flags belong on a separate
        // computed-state filter, not the Status enum.
        _                   => null,
    };
}
