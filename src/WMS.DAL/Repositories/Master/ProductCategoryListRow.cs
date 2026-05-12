namespace WMS.DAL.Repositories.Master;

// Read-only projection for Category list pages (/Categories index).
// ParentName denormalized from a LEFT JOIN so the list view can show
// "Lipstick (under Makeup)" without resolving Ids client-side.
//
// Path is surfaced verbatim — the Index view renders it with monospace
// styling so subtree groupings stay visually adjacent.
public sealed record ProductCategoryListRow(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId,
    string? ParentName,
    string? Path,
    string? Description,
    int? DisplayOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// Server-side paging/filter shape for /Categories/Data.
// ParentId nullable in a tri-state way:
//   * Guid.Empty (or null) — no parent filter
//   * Specific Guid — filter to children of that parent
//   * "Roots only" handled via a separate bool flag below
public sealed record ProductCategoryFilter(
    int Page,
    int PageSize,
    string? Search,
    bool? IsActive,
    Guid? ParentId,
    bool RootsOnly,
    string? SortBy,
    bool SortDesc);

// Chip-count envelope. Status counts; tree-shape stats (Roots/Children)
// come along for the secondary chip row.
public sealed record ProductCategoryStatusCounts(
    int All,
    int Active,
    int Inactive,
    int Roots);

// Used by the Edit form's parent-picker dropdown — excludes self +
// descendants to prevent obvious cycles before the trigger fires.
public sealed record ProductCategoryPickerItem(
    Guid Id,
    string Code,
    string Name,
    string? Path);
