using System.Data;
using Dapper;
using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

internal sealed class UomRepository : IUomRepository
{
    private readonly IDbConnection _connection;

    public UomRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // IX_UnitsOfMeasure_Active(IsActive, Code) covers WHERE+ORDER
        // exactly.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.UnitsOfMeasure
              WHERE IsActive = 1
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }
}
