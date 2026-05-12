namespace WMS.DAL.Repositories.Master;

// Read-only projection for Zone list pages. Denormalizes WarehouseCode
// + WarehouseName so the Index can group/display zones by parent
// warehouse without resolving Ids client-side.
public sealed record ZoneListRow(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    string Code,
    string Name,
    string Type,
    string? Temperature,
    bool LotCommingleAllowed,
    bool IsActive,
    int LocationCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// Server-side paging/filter shape for /Zones/Data.
//   WarehouseId: nullable — single-warehouse drill-down.
//   Type: nullable — Zone Type chip filter (8 values).
public sealed record ZoneFilter(
    int Page,
    int PageSize,
    string? Search,
    bool? IsActive,
    Guid? WarehouseId,
    string? Type,
    string? SortBy,
    bool SortDesc);

// Chip-count envelope. Status counts; Type counts power a secondary
// chip strip (Receiving/Storage/Picking/Packing/etc.).
public sealed record ZoneStatusCounts(
    int All,
    int Active,
    int Inactive,
    int Receiving,
    int Storage,
    int Picking,
    int Packing,
    int Shipping,
    int Staging,
    int Quarantine,
    int Returns);
