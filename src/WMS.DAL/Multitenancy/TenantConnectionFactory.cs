using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using WMS.Common.Multitenancy;

namespace WMS.DAL.Multitenancy;

// Resolves a tenant's per-DB connection string by reading master.Tenants
// (Id, DatabaseName, Status) then formatting the configured template
// (e.g. "Server=...;Database={0};...").
//
// Cached in IMemoryCache with 5-minute sliding expiration — covers the
// common case of request bursts from one tenant while still picking up
// DatabaseName changes within minutes (no app restart needed). The
// underlying SqlConnection pool handles per-connection reuse below this
// layer.
//
// ConnectionStringEncrypted (VARBINARY) is reserved for future encrypted
// per-tenant credentials and is intentionally not consulted here.
public sealed class TenantConnectionFactory : ITenantConnectionFactory
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly string _masterConnectionString;
    private readonly string _tenantTemplate;
    private readonly IMemoryCache _cache;

    public TenantConnectionFactory(
        string masterConnectionString,
        string tenantTemplate,
        IMemoryCache cache)
    {
        if (string.IsNullOrWhiteSpace(masterConnectionString))
            throw new ArgumentException(
                "Master connection string is required.", nameof(masterConnectionString));
        if (!tenantTemplate.Contains("{0}"))
            throw new ArgumentException(
                "Tenant template must contain '{0}' placeholder for the database name.",
                nameof(tenantTemplate));

        _masterConnectionString = masterConnectionString;
        _tenantTemplate = tenantTemplate;
        _cache = cache;
    }

    public IDbConnection CreateConnection(Guid tenantId)
    {
        var connString = _cache.GetOrCreate(
            CacheKey(tenantId),
            entry =>
            {
                entry.SlidingExpiration = CacheTtl;
                return ResolveConnectionString(tenantId);
            })!;
        return new SqlConnection(connString);
    }

    private static string CacheKey(Guid tenantId) => $"tenant:{tenantId:N}:conn";

    private string ResolveConnectionString(Guid tenantId)
    {
        using var master = new SqlConnection(_masterConnectionString);
        var dbName = master.QueryFirstOrDefault<string>(
            "SELECT DatabaseName FROM master.Tenants " +
            "WHERE Id = @id AND Status = 'Active'",
            new { id = tenantId });

        if (string.IsNullOrEmpty(dbName))
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' not found or is not Active.");

        return string.Format(_tenantTemplate, dbName);
    }
}
