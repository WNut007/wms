using System.Data;
using Dapper;
using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

internal sealed class CarrierRepository : ICarrierRepository
{
    private readonly IDbConnection _connection;

    public CarrierRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // IX_Carriers_Status(Status, IsActive, Code) covers exactly.
        // Production-only — admins should not pick a Configured /
        // Tested carrier as a customer's preferred (would route real
        // shipments to a not-yet-validated integration).
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Carriers
              WHERE Status = 'Production' AND IsActive = 1
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }
}
