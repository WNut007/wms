using WMS.Common.Auth;

namespace WMS.BLL.Services.Auth;

// Permission resolution + caching layer. Reads through
// IPermissionRepositoryFactory the first time a (tenantId, userId)
// is asked for, then serves the resolved list from IMemoryCache for
// the next 15 minutes (sliding).
public interface IPermissionService
{
    // Every effective (Function, Action) the user is granted, or an
    // empty list if the user has no roles / matching grants.
    Task<IReadOnlyList<UserPermission>> GetForUserAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default);

    // Convenience predicate for filters / view helpers — equivalent
    // to GetForUserAsync(...).Any(p => p.FunctionCode == ... && p.Action == ...).
    Task<bool> HasPermissionAsync(
        Guid userId,
        Guid tenantId,
        string functionCode,
        string action,
        CancellationToken ct = default);
}
