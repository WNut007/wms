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
