namespace WMS.Web.Services.Mappers;

// bool ↔ "active"/"inactive" translation between master.BoxTypes
// .IsActive and the wire format the /BoxTypes front-end speaks.
//
// Same shape as WarehouseStatusMapper — BoxTypes uses IsActive as the
// canonical archive toggle (Migration_010 has no Status string column).
public static class BoxTypeStatusMapper
{
    public static string ToWire(bool isActive) =>
        isActive ? "active" : "inactive";

    // null / "all" / empty / unknown → null (= no filter applied).
    public static bool? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "active"            => true,
        "inactive"          => false,
        _                   => null,
    };
}
