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

    public async Task<IReadOnlyList<StockMovementListRow>> GetByProductAsync(
        Guid productId,
        DateTime? since = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        // 2-step seek for the inventory rows (IX_Stock_Product →
        // per-Stock history via IX_StockMovements_Stock), plus three
        // LEFT JOINs to resolve display fields:
        //   - master.Locations × 2 for From/To codes (NULL when the
        //     movement has no source/destination — Receive has no
        //     FromLocationId, etc.)
        //   - security.Users for PerformedByName, COALESCE'd through
        //     FullName → Email → 'System' so the title is never blank
        //     even when PerformedBy itself is NULL (system actions).
        // All three JOINs hit PK indexes — cheap at 20-row pages.
        var rows = await _connection.QueryAsync<StockMovementListRow>(new CommandDefinition(
            @"SELECT m.Id,
                     m.MovementType,
                     m.QuantityDelta,
                     fl.Code AS FromLocationCode,
                     tl.Code AS ToLocationCode,
                     m.ReferenceType,
                     m.Notes,
                     COALESCE(u.FullName, u.Email, 'System') AS PerformedByName,
                     m.PerformedAt
              FROM inventory.StockMovements m
              JOIN inventory.Stock s        ON s.Id  = m.StockId
              LEFT JOIN master.Locations fl ON fl.Id = m.FromLocationId
              LEFT JOIN master.Locations tl ON tl.Id = m.ToLocationId
              LEFT JOIN security.Users u    ON u.Id  = m.PerformedBy
              WHERE s.ProductId = @productId
                AND (@since IS NULL OR m.PerformedAt >= @since)
              ORDER BY m.PerformedAt DESC
              OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            new { productId, since, limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StockMovementListRow>> GetByWarehouseAsync(
        Guid warehouseId, DateTime? since = null, int limit = 20,
        CancellationToken ct = default)
    {
        // Movement-belongs-to-warehouse via Stock.LocationId →
        // Locations.WarehouseId. Same Users + Locations LEFT JOINs as
        // GetByProductAsync for display-field resolution. Cross-
        // warehouse Transfers naturally show twice (once per side)
        // when both endpoints are in the same warehouse, and only
        // once otherwise — which matches the per-warehouse view's
        // intent (you see what touched THIS warehouse).
        var rows = await _connection.QueryAsync<StockMovementListRow>(new CommandDefinition(
            @"SELECT m.Id,
                     m.MovementType,
                     m.QuantityDelta,
                     fl.Code AS FromLocationCode,
                     tl.Code AS ToLocationCode,
                     m.ReferenceType,
                     m.Notes,
                     COALESCE(u.FullName, u.Email, 'System') AS PerformedByName,
                     m.PerformedAt
              FROM inventory.StockMovements m
              JOIN inventory.Stock s     ON s.Id  = m.StockId
              JOIN master.Locations sl   ON sl.Id = s.LocationId
              LEFT JOIN master.Locations fl ON fl.Id = m.FromLocationId
              LEFT JOIN master.Locations tl ON tl.Id = m.ToLocationId
              LEFT JOIN security.Users u    ON u.Id  = m.PerformedBy
              WHERE sl.WarehouseId = @warehouseId
                AND (@since IS NULL OR m.PerformedAt >= @since)
              ORDER BY m.PerformedAt DESC
              OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            new { warehouseId, since, limit },
            cancellationToken: ct));
        return rows.AsList();
    }
}
