using System.Data;
using Dapper;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Dapper-backed reader for inventory.StockMovements. Bound to a
// single tenant DB connection in its ctor; the factory creates one
// per tenantId via ITenantConnectionFactory. Mirrors
// WarehouseRepository / StockRepository shape.
//
// No tenant filter on any query — the connection IS the tenant
// scope (DB-per-tenant per ADR-001).
//
// Dapper materialises VARCHAR MovementType into the
// StockMovementType enum automatically via Enum.Parse — no type
// handler needed.
internal sealed class StockMovementRepository : IStockMovementRepository
{
    private const string SelectColumns = @"
        SELECT Id, StockId, MovementType, FromLocationId, ToLocationId,
               QuantityDelta, UomId, OwnerId,
               ReferenceType, ReferenceId, Notes,
               PerformedBy, PerformedAt
        FROM inventory.StockMovements";

    private readonly IDbConnection _connection;

    public StockMovementRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<StockMovement>> GetByStockAsync(
        Guid stockId, int limit = 50, CancellationToken ct = default)
    {
        // IX_StockMovements_Stock (StockId, PerformedAt DESC) covers
        // this exactly — every Activity panel hit for a Stock row.
        var rows = await _connection.QueryAsync<StockMovement>(new CommandDefinition(
            SelectColumns + @"
            WHERE StockId = @stockId
            ORDER BY PerformedAt DESC
            OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            new { stockId, limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StockMovement>> GetByReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken ct = default)
    {
        // Uses partial IX_StockMovements_Reference (WHERE ReferenceId
        // IS NOT NULL). Putaway rows with NULL ReferenceId are not
        // findable here — by design (TD-004).
        var rows = await _connection.QueryAsync<StockMovement>(new CommandDefinition(
            SelectColumns + @"
            WHERE ReferenceType = @referenceType
              AND ReferenceId   = @referenceId
            ORDER BY PerformedAt DESC",
            new { referenceType, referenceId },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StockMovement>> GetByProductAsync(
        Guid productId,
        DateTime? since = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        // 2-step seek — IX_Stock_Product (ProductId) hits the Stock
        // rows for this product, then per-Stock history via the
        // covering index. Acceptable for Phase 1 read patterns; if
        // reports get slow, denormalise ProductId onto StockMovements
        // in a follow-up migration.
        var rows = await _connection.QueryAsync<StockMovement>(new CommandDefinition(
            @"SELECT m.Id, m.StockId, m.MovementType,
                     m.FromLocationId, m.ToLocationId,
                     m.QuantityDelta, m.UomId, m.OwnerId,
                     m.ReferenceType, m.ReferenceId, m.Notes,
                     m.PerformedBy, m.PerformedAt
              FROM inventory.StockMovements m
              JOIN inventory.Stock s ON s.Id = m.StockId
              WHERE s.ProductId = @productId
                AND (@since IS NULL OR m.PerformedAt >= @since)
              ORDER BY m.PerformedAt DESC
              OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            new { productId, since, limit },
            cancellationToken: ct));
        return rows.AsList();
    }
}
