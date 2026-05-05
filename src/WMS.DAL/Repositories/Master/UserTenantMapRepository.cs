using Dapper;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed master.UserTenantMap reader. The JOIN against
// master.Tenants filters out non-Active tenants (Suspended / Inactive)
// and pulls Code + Name in one round-trip so callers don't have to
// re-query for display fields.
//
// Ordering — IsDefault rows first so AuthenticateAsync verifies the
// password against the user's primary tenant when one is flagged;
// alphabetical Code as tiebreaker for determinism.
public sealed class UserTenantMapRepository : IUserTenantMapRepository
{
    private readonly IMasterConnectionFactory _master;

    public UserTenantMapRepository(IMasterConnectionFactory master) => _master = master;

    public async Task<IReadOnlyList<UserTenantInfo>> GetByEmailAsync(
        string email,
        CancellationToken ct = default)
    {
        using var conn = _master.CreateConnection();
        var rows = await conn.QueryAsync<UserTenantInfo>(new CommandDefinition(
            @"SELECT t.Id   AS TenantId,
                     t.Code AS TenantCode,
                     t.Name AS TenantName
              FROM master.UserTenantMap m
              JOIN master.Tenants t ON t.Id = m.TenantId
              WHERE m.UserEmail = @email
                AND t.Status = 'Active'
              ORDER BY m.IsDefault DESC, t.Code ASC",
            new { email },
            cancellationToken: ct));
        return rows.AsList();
    }
}
