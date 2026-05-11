using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

// Phase 24 — Role CRUD surface. T1 lands the reads + ADMIN-code lookup
// needed by Users module. T2 extends with permission-matrix writes +
// system-role guards.
public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetActiveAsync(CancellationToken ct = default);

    // Full list for the /Roles index (system + custom). UserCount pre-
    // aggregated server-side via correlated subquery so each row
    // doesn't issue a per-row count.
    Task<IReadOnlyList<RoleListRow>> GetAllAsync(CancellationToken ct = default);

    // Permission matrix read for /Roles/Detail/{id}. Returns one row per
    // active Function — LEFT JOIN means functions with no grant yet
    // come back with all flags = false.
    Task<IReadOnlyList<RolePermissionRow>> GetPermissionsForRoleAsync(
        Guid roleId,
        CancellationToken ct = default);

    // T2 — permission editor surface. Update flag set on an existing
    // (Role, Function) row; insert if it doesn't exist yet.
    Task UpsertPermissionAsync(
        Guid roleId,
        Guid functionId,
        bool canView,
        bool canAdd,
        bool canEdit,
        bool canDelete,
        bool canApprove,
        Guid? actorId,
        CancellationToken ct = default);
}

public sealed record RoleListRow(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive,
    int UserCount,
    int PermissionCount,
    DateTime CreatedAt);

public sealed record RolePermissionRow(
    Guid FunctionId,
    string FunctionCode,
    string FunctionName,
    string Module,
    int DisplayOrder,
    bool CanView,
    bool CanAdd,
    bool CanEdit,
    bool CanDelete,
    bool CanApprove);
