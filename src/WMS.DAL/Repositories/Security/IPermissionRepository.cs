using WMS.Common.Auth;

namespace WMS.DAL.Repositories.Security;

// Tenant-scoped reader for the permission matrix. Resolves every
// (Function, Action) the user has — already MAX-aggregated across all
// of the user's roles.
public interface IPermissionRepository
{
    Task<IReadOnlyList<UserPermission>> GetForUserAsync(
        Guid userId,
        CancellationToken ct = default);
}
