namespace WMS.Web.Services.Mappers;

// Phase 24 — wire ↔ display for the User status filter chip.
// Statuses are derived (IsActive + LockedUntil), not stored as an enum
// column, so this mapper just normalises the lowercase wire format
// to the closed set the repo's WHERE clause understands.
public static class UserStatusMapper
{
    public const string Active   = "active";
    public const string Inactive = "inactive";
    public const string Locked   = "locked";

    public static string? FromWire(string? wire) =>
        wire switch
        {
            Active or Inactive or Locked => wire,
            _ => null,  // unknown → no filter
        };

    // Variant for the chip badge styling.
    public static string Variant(string status) =>
        status switch
        {
            "Active"   => "s-success",
            "Inactive" => "s-neutral",
            "Locked"   => "s-danger",
            _ => "s-neutral",
        };
}
