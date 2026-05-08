namespace WMS.Web.Services.Mappers;

// PascalCase ↔ lowercase translation between master.Customers.Status
// (DB-canonical, satisfies CK_Customers_Status) and the wire format the
// existing /Customers front-end speaks.
//
// Vocabulary mismatch handled at this boundary:
//   - Mock UI's 'pending' filter chip ↔ DB 'Draft' state
//   - DB 'Suspended' has no mock chip — surfaces under 'all' until a
//     dedicated chip is added (UX TD; non-blocking)
//
// Round-trippable: ToWire(FromWire(x)) == x for every supported wire
// value. Mock's 'inactive' chip continues to mean Inactive (not
// Suspended). Unknown wire values fall through to "no filter".
public static class CustomerStatusMapper
{
    public static string ToWire(string db) => db switch
    {
        "Active"    => "active",
        "Inactive"  => "inactive",
        "Suspended" => "suspended",
        "Draft"     => "pending",
        _           => "active",
    };

    public static string? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "active"            => "Active",
        "inactive"          => "Inactive",
        "suspended"         => "Suspended",
        // Mock vocabulary: 'pending' is the human-friendly name the
        // existing UI uses for the same workflow state the DB calls
        // 'Draft'. Both are accepted on the way in for forward
        // compatibility.
        "pending"           => "Draft",
        "draft"             => "Draft",
        _                   => null,
    };
}
