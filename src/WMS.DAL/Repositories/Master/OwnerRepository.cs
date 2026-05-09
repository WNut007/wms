using System.Data;
using Dapper;
using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

internal sealed class OwnerRepository : IOwnerRepository
{
    private readonly IDbConnection _connection;

    public OwnerRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveSuppliersAsync(CancellationToken ct = default)
    {
        // IX_Owners_Type(OwnerType, IsActive) covers WHERE exactly.
        // Code ASC for deterministic ordering — admins scanning the
        // Vendor dropdown read top-to-bottom.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Owners
              WHERE OwnerType IN ('Supplier', 'VMI') AND IsActive = 1
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }
}
