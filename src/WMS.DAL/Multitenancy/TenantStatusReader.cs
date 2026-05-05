using Dapper;
using Microsoft.Extensions.Caching.Memory;
using WMS.Common.Multitenancy;

namespace WMS.DAL.Multitenancy;

// Active-status reader with a 5-minute sliding cache. Caches *only* the
// "active" outcome — suspended / inactive / unknown tenants fall through
// to a fresh master DB read on every request. Trade-off:
//   * Active tenants (~all of them in normal operation) cost one master
//     DB read per 5 idle minutes.
//   * A tenant that gets suspended takes effect on the next request from
//     anyone whose cache entry has expired (worst case 5 minutes), but
//     once they're signed out the cookie is gone and no further requests
//     carry the stale claim.
//   * A tenant that's *already* suspended pays a master DB read every
//     time someone tries to use a stale cookie — acceptable because the
//     branch ends in a sign-out anyway.
public sealed class TenantStatusReader : ITenantStatusReader
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IMasterConnectionFactory _master;
    private readonly IMemoryCache _cache;

    public TenantStatusReader(IMasterConnectionFactory master, IMemoryCache cache)
    {
        _master = master;
        _cache = cache;
    }

    public async Task<bool> IsActiveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var key = CacheKey(tenantId);
        if (_cache.TryGetValue<bool>(key, out var cached) && cached)
            return true;

        using var conn = _master.CreateConnection();
        var status = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT Status FROM master.Tenants WHERE Id = @id",
            new { id = tenantId },
            cancellationToken: ct));

        if (status == "Active")
        {
            _cache.Set(key, true, new MemoryCacheEntryOptions
            {
                SlidingExpiration = CacheTtl
            });
            return true;
        }
        return false;
    }

    private static string CacheKey(Guid tenantId) => $"tenant-active:{tenantId:N}";
}
