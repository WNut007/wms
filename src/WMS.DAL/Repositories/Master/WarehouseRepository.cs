using System.Data;
using Dapper;
using WMS.Common.Auth;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader for master.Warehouses. Bound to a single tenant
// DB connection in its ctor; the factory creates one per tenantId via
// ITenantConnectionFactory.
internal sealed class WarehouseRepository : IWarehouseRepository
{
    private readonly IDbConnection _connection;

    public WarehouseRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<WarehouseInfo>> GetActiveAsync(CancellationToken ct = default)
    {
        var rows = await _connection.QueryAsync<WarehouseInfo>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Warehouses
              WHERE IsActive = 1
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }
}
