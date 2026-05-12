namespace WMS.DAL.Repositories.Master;

// Read-only projection for Carrier list pages (/Carriers index).
public sealed record CarrierListRow(
    Guid Id,
    string Code,
    string Name,
    string? Type,
    string? Country,
    string Status,
    int? DisplayOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// Server-side paging/filter shape. Status filter takes one of the 4
// CK values; IsActive nullable for "active-only" / "inactive-only" /
// "any" facet (less used than Status but cheap to support).
public sealed record CarrierFilter(
    int Page,
    int PageSize,
    string? Search,
    string? Status,
    bool? IsActive,
    string? Type,
    string? SortBy,
    bool SortDesc);

// Chip-count envelope. Status has 5 chips (All + 4 lifecycle states);
// Active/Inactive shown as secondary stats but not as primary chips
// (the lifecycle Status is the more meaningful filter).
public sealed record CarrierStatusCounts(
    int All,
    int Inactive,
    int Configured,
    int Tested,
    int Production);
