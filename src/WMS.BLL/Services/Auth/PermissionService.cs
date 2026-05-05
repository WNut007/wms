using Microsoft.Extensions.Caching.Memory;
using WMS.Common.Auth;
using WMS.DAL.Repositories.Security;

namespace WMS.BLL.Services.Auth;

// IMemoryCache-backed wrapper around IPermissionRepositoryFactory.
//
// Cache window — 15 minutes sliding (matches the Auth Architecture
// decision in CLAUDE.md). Worst-case staleness when an admin grants
// or revokes a permission is one window; explicit invalidation on
// role-management actions is a separate concern (admin chunk).
//
// Cache key shape: "perms:{tenantId:N}:{userId:N}". Tenant included
// so two tenants sharing a user email never collide on cache.
public sealed class PermissionService : IPermissionService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private readonly IPermissionRepositoryFactory _repoFactory;
    private readonly IMemoryCache _cache;

    public PermissionService(
        IPermissionRepositoryFactory repoFactory,
        IMemoryCache cache)
    {
        _repoFactory = repoFactory;
        _cache = cache;
    }

    public async Task<IReadOnlyList<UserPermission>> GetForUserAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var cached = await _cache.GetOrCreateAsync(
            CacheKey(tenantId, userId),
            async entry =>
            {
                entry.SlidingExpiration = CacheTtl;
                var repo = _repoFactory.For(tenantId);
                return await repo.GetForUserAsync(userId, ct);
            });
        return cached ?? Array.Empty<UserPermission>();
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        Guid tenantId,
        string functionCode,
        string action,
        CancellationToken ct = default)
    {
        var perms = await GetForUserAsync(userId, tenantId, ct);
        for (var i = 0; i < perms.Count; i++)
        {
            var p = perms[i];
            if (p.FunctionCode == functionCode && p.Action == action)
                return true;
        }
        return false;
    }

    private static string CacheKey(Guid tenantId, Guid userId) =>
        $"perms:{tenantId:N}:{userId:N}";
}
