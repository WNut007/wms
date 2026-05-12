namespace WMS.DAL.Repositories.Master;

// Read-only projection for Location list pages.
// Denormalizes WarehouseCode + ZoneCode via INNER JOINs so the Index
// view shows the bin's full parent path without resolving Ids client-side.
public sealed record LocationListRow(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ZoneId,
    string ZoneCode,
    string ZoneType,
    string Code,
    string? Description,
    string CapacityPolicy,
    int BinRank,
    bool IsPickface,
    string Status,
    bool IsActive,
    int StockRows,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// Server-side paging/filter shape for /Locations/Data.
public sealed record LocationFilter(
    int Page,
    int PageSize,
    string? Search,
    bool? IsActive,
    Guid? WarehouseId,
    Guid? ZoneId,
    string? Status,
    string? SortBy,
    bool SortDesc);

// Chip-count envelope. Status chips (3 states) + active/inactive +
// pickface count for at-a-glance "how many pick faces in this WH".
public sealed record LocationStatusCounts(
    int All,
    int Active,
    int Inactive,
    int StatusActive,
    int StatusBlocked,
    int StatusMaintenance,
    int Pickfaces);
