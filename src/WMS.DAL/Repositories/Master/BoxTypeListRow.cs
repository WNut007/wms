namespace WMS.DAL.Repositories.Master;

// Read-only projection for BoxType list pages (/BoxTypes index).
//
// Same shape as the entity for now — no JOIN aggregates. Once usage
// counts matter (how many PackTasks have used this BoxType?), a
// LEFT JOIN aggregate column can be added without touching the entity.
public sealed record BoxTypeListRow(
    Guid Id,
    string Code,
    string Name,
    decimal? InternalLengthCm,
    decimal? InternalWidthCm,
    decimal? InternalHeightCm,
    decimal? ExternalLengthCm,
    decimal? ExternalWidthCm,
    decimal? ExternalHeightCm,
    decimal? EmptyWeightKg,
    decimal? MaxLoadKg,
    decimal? UnitCost,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// Server-side paging/filter shape for /BoxTypes/Data JSON endpoint.
// IsActive nullable: null = "no filter", true/false = explicit chip.
public sealed record BoxTypeFilter(
    int Page,
    int PageSize,
    string? Search,
    bool? IsActive,
    string? SortBy,
    bool SortDesc);

// Chip-count envelope for the /BoxTypes Index page filter strip.
// Mirrors Phase 9A pattern (PurchaseOrderStatusCounts etc.).
public sealed record BoxTypeStatusCounts(
    int All,
    int Active,
    int Inactive);
