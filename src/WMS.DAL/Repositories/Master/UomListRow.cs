namespace WMS.DAL.Repositories.Master;

// Read-only projection for UoM list pages (/Uoms index).
//
// No JOIN aggregates today. A future "ProductCount" column (how many
// Products use this as BaseUom) can be added without touching the
// entity.
public sealed record UomListRow(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsBase,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// Server-side paging/filter shape for /Uoms/Data.
// Type nullable: null = "no filter", set = specific Type chip.
public sealed record UomFilter(
    int Page,
    int PageSize,
    string? Search,
    bool? IsActive,
    string? Type,
    string? SortBy,
    bool SortDesc);

// Chip-count envelope. Status counts mirror BoxType pattern; Type
// counts power a second filter row showing how many UoMs exist per Type.
public sealed record UomStatusCounts(
    int All,
    int Active,
    int Inactive,
    int Count,
    int Weight,
    int Volume,
    int Length);
