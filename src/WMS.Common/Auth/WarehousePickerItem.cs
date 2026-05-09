namespace WMS.Common.Auth;

// Richer projection for the Step 3 warehouse picker (Phase 8.5).
// Extends the legacy 3-field WarehouseInfo (Id, Code, Name) with
// Address + Type so the picker can render city, region grouping,
// and the warehouse type badge without a follow-up query.
//
// Distinct from WarehouseInfo intentionally — that record is consumed
// by other surfaces (cookie claims, simple pickers) and adding fields
// would force every consumer to handle the new shape. Two records,
// each carrying exactly what its consumer needs.
public sealed record WarehousePickerItem(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    string Type);
