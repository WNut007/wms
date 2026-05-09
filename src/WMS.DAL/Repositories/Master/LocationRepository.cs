using System.Data;
using Dapper;
using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

internal sealed class LocationRepository : ILocationRepository
{
    private readonly IDbConnection _connection;

    public LocationRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveByWarehouseAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Locations
              WHERE WarehouseId = @warehouseId
                AND IsActive = 1
                AND Status = 'Active'
              ORDER BY Code",
            new { warehouseId },
            cancellationToken: ct));
        return rows.AsList();
    }
}
