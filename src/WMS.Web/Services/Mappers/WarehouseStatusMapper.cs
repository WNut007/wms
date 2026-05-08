namespace WMS.Web.Services.Mappers;

// bool ↔ "active"/"inactive" translation between master.Warehouses
// .IsActive and the wire format the existing /Warehouses front-end
// speaks.
//
// Mock UI's third 'maintenance' chip is intentionally absorbed here
// without distinction — schema is bool-only; introducing a third
// Status state is a separate concern (TD-009). Frontend may keep
// emitting status=maintenance; this mapper drops it (= no filter
// applied) so the page still works.
public static class WarehouseStatusMapper
{
    public static string ToWire(bool isActive) =>
        isActive ? "active" : "inactive";

    // null / "all" / empty / unknown → null (= no filter applied).
    public static bool? FromWire(string? wire) => wire?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "active"            => true,
        "inactive"          => false,
        // 'maintenance' falls here — TD-009 will decide whether to add
        // a third state or surface the column another way. Until then,
        // drop the filter rather than misclassify.
        _                   => null,
    };
}
