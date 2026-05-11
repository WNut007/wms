using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

// Read-projection for the role-assignment surface. Carries Role.Code +
// Name resolved server-side so the per-user view doesn't issue a per-
// row lookup.
public sealed record UserRoleAssignment(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    bool IsSystemRole,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    Guid? AssignedBy,
    DateTime CreatedAt);

// Tenant-scoped CRUD over security.UserRoles.
public interface IUserRoleRepository
{
    Task<IReadOnlyList<UserRoleAssignment>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetRoleIdsByUserAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(UserRole assignment, CancellationToken ct = default);
    Task RemoveAsync(Guid userId, Guid roleId, CancellationToken ct = default);

    // Diff-then-apply for the "save user with checked role boxes" path.
    // Inserts missing rows + deletes removed rows in one round-trip
    // pair. Returns (added, removed) counts so service layer can log.
    Task<(int Added, int Removed)> ReplaceForUserAsync(
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        Guid? assignedBy,
        CancellationToken ct = default);
}
