namespace WMS.DAL.Repositories.Security;

// Phase 24 — read-projection for /Users list page. Carries display
// fields the page needs without pulling PasswordHash through the wire.
// RoleCodes pre-aggregated server-side (STRING_AGG) so each row renders
// without a per-row UserRoles fetch.
public sealed record UserListRow(
    Guid Id,
    string Email,
    string? FullName,
    bool IsActive,
    DateTime? LastLoginAt,
    int FailedLoginAttempts,
    DateTime? LockedUntil,
    string? RoleCodes,           // comma-separated 'ADMIN,MANAGER' or NULL
    DateTime CreatedAt);

// Filter shape for IUserRepository.GetPagedAsync. Mirrors the chip-
// counts pattern from other admin lists.
public sealed record UserFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,         // matches Email OR FullName
    string? Status = null,         // 'active' | 'inactive' | 'locked'
    string? RoleCode = null,       // single role code filter
    string SortBy = "email",
    bool SortDesc = false);

// Phase 24 — chip-count aggregate for /Users list. Counts respect
// Search but ignore Status so inactive chips still display totals.
public sealed record UserStatusCounts(
    int All,
    int Active,
    int Inactive,
    int Locked);
