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
        // master.Locations has Code (NVARCHAR 20) + Description
        // (NVARCHAR 200, nullable) per Migration 007 — no `Name`
        // column. LookupItem's positional ctor expects (Id, Code, Name)
        // so we COALESCE Description → Code to satisfy the third
        // parameter without leaving NULLs for un-described locations.
        // Caught at runtime smoke (Phase 9C post-tag); the repo's
        // class+setter cousins (Owner/Carrier/Product) all have a
        // real Name column so the inconsistency wasn't obvious in
        // Phase 9B authoring.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, COALESCE(Description, Code) AS Name
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
