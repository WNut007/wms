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

    // MERGE WITH (HOLDLOCK) is the single-statement atomic upsert SQL
    // Server endorses for this pattern: HOLDLOCK serializes concurrent
    // executions on the same key range, eliminating the classic
    // SELECT-then-INSERT race. NULL-safe key matching mirrors
    // GetByKeyAsync above. OUTPUT inserted.* gives us back the row in
    // either branch (insert returns the new row, update returns the
    // post-update row), so the caller never has to round-trip again
    // for the resulting Id / Version.
    public Task<Stock> UpsertOnHandAsync(
        StockKey key,
        decimal quantityDelta,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.QuerySingleAsync<Stock>(new CommandDefinition(
            @"MERGE inventory.Stock WITH (HOLDLOCK) AS target
              USING (
                  SELECT @LocationId AS LocationId,
                         @ProductId  AS ProductId,
                         @LotId      AS LotId,
                         @PalletId   AS PalletId,
                         @OwnerId    AS OwnerId,
                         @UomId      AS UomId
              ) AS src
              ON  target.LocationId = src.LocationId
              AND target.ProductId  = src.ProductId
              AND ((src.LotId    IS NULL AND target.LotId    IS NULL) OR target.LotId    = src.LotId)
              AND ((src.PalletId IS NULL AND target.PalletId IS NULL) OR target.PalletId = src.PalletId)
              AND target.OwnerId    = src.OwnerId
              AND target.UomId      = src.UomId
              WHEN MATCHED THEN
                  UPDATE SET
                      QuantityOnHand = target.QuantityOnHand + @Delta,
                      LastMovementAt = SYSUTCDATETIME(),
                      UpdatedAt      = SYSUTCDATETIME(),
                      UpdatedBy      = @UserId,
                      Version        = target.Version + 1
              WHEN NOT MATCHED THEN
                  INSERT (LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
                          QuantityOnHand, QuantityAllocated, LastMovementAt,
                          CreatedAt, CreatedBy)
                  VALUES (src.LocationId, src.ProductId, src.LotId, src.PalletId,
                          src.OwnerId, src.UomId,
                          @Delta, 0, SYSUTCDATETIME(),
                          SYSUTCDATETIME(), @UserId)
              OUTPUT inserted.Id, inserted.LocationId, inserted.ProductId,
                     inserted.LotId, inserted.PalletId,
                     inserted.OwnerId, inserted.UomId,
                     inserted.QuantityOnHand, inserted.QuantityAllocated,
                     inserted.LastMovementAt,
                     inserted.CreatedAt, inserted.UpdatedAt,
                     inserted.CreatedBy, inserted.UpdatedBy,
                     inserted.Version;",
            new
            {
                key.LocationId,
                key.ProductId,
                key.LotId,
                key.PalletId,
                key.OwnerId,
                key.UomId,
                Delta = quantityDelta,
                UserId = userId,
            },
            cancellationToken: ct));
}
