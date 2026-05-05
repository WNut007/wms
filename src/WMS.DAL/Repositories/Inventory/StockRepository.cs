using System.Data;
using Dapper;
using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Dapper-backed reader for inventory.Stock. Bound to a single tenant
// DB connection; the factory creates one per tenantId via
// ITenantConnectionFactory.
internal sealed class StockRepository : IStockRepository
{
    // Column list shared across all SELECTs so the dapper materializer
    // hits properties by name without surprises.
    private const string SelectColumns = @"
        SELECT Id, LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
               QuantityOnHand, QuantityAllocated, LastMovementAt,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inventory.Stock";

    private readonly IDbConnection _connection;

    public StockRepository(IDbConnection connection) => _connection = connection;

    public Task<Stock?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Stock?>(new CommandDefinition(
            SelectColumns + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public async Task<IReadOnlyList<Stock>> GetByLocationAsync(
        Guid locationId, CancellationToken ct = default) =>
        (await _connection.QueryAsync<Stock>(new CommandDefinition(
            SelectColumns + " WHERE LocationId = @locationId",
            new { locationId },
            cancellationToken: ct))).AsList();

    public async Task<IReadOnlyList<Stock>> GetByProductAsync(
        Guid productId, CancellationToken ct = default) =>
        (await _connection.QueryAsync<Stock>(new CommandDefinition(
            SelectColumns + " WHERE ProductId = @productId",
            new { productId },
            cancellationToken: ct))).AsList();

    // NULL-safe match on LotId + PalletId — passing NULL for either
    // matches a row that is itself NULL there, instead of the SQL
    // default "NULL = NULL → unknown → not matched".
    public Task<Stock?> GetByKeyAsync(StockKey key, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Stock?>(new CommandDefinition(
            SelectColumns + @"
            WHERE LocationId = @LocationId
              AND ProductId  = @ProductId
              AND ((@LotId    IS NULL AND LotId    IS NULL) OR LotId    = @LotId)
              AND ((@PalletId IS NULL AND PalletId IS NULL) OR PalletId = @PalletId)
              AND OwnerId    = @OwnerId
              AND UomId      = @UomId",
            new
            {
                key.LocationId,
                key.ProductId,
                key.LotId,
                key.PalletId,
                key.OwnerId,
                key.UomId,
            },
            cancellationToken: ct));
}
