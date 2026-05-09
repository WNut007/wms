using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Tenant-scoped API for inventory.Stock. Reads are split-clean from the
// upsert primitive below; richer mutation primitives (putaway, pick,
// adjust, reserve) live in their own repos / services per ADR.
public interface IStockRepository
{
    Task<Stock?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Stock>> GetByLocationAsync(Guid locationId, CancellationToken ct = default);
    Task<IReadOnlyList<Stock>> GetByProductAsync(Guid productId, CancellationToken ct = default);

    // NULL-safe lookup on the 6-tuple key — see implementation for
    // the SQL pattern that handles nullable LotId / PalletId.
    Task<Stock?> GetByKeyAsync(StockKey key, CancellationToken ct = default);

    // Atomic receive primitive. Adds quantityDelta to the stock row at
    // the 6-tuple key, creating it on first arrival. Implemented as a
    // MERGE WITH (HOLDLOCK) wrapped in a transaction with the
    // matching StockMovements INSERT so concurrent receives at the
    // same key serialize safely AND every Stock change has a paired
    // movement row (ADR-014). quantityDelta is expected to be
    // positive (receiving deposits stock, never removes it); the
    // service layer enforces that.
    //
    // movementCtx carries MovementType (Receive / Adjust+ for future),
    // PerformedBy, and provenance (ReferenceType + ReferenceId). The
    // userId previously passed standalone is now in ctx.PerformedBy.
    Task<Stock> UpsertOnHandAsync(
        StockKey key,
        decimal quantityDelta,
        StockMovementContext movementCtx,
        CancellationToken ct = default);

    // Atomic put-away / transfer primitive. Decrements OnHand at the
    // source stock row and adds the same quantity onto the row at
    // (toLocationId, sourceProduct, sourceLot, sourcePallet,
    // sourceOwner, sourceUom), creating the destination row if it
    // doesn't yet exist.
    //
    // Writes TWO StockMovements rows inside the same transaction —
    // one with QuantityDelta = -quantity against the source StockId,
    // one with +quantity against the destination StockId. Both share
    // movementCtx's ReferenceType/ReferenceId so they reconcile in
    // reports.
    //
    // SQL Server raises (THROW 50001..50003) if:
    //   * source row is missing
    //   * destination location matches source (no-op refused)
    //   * source has insufficient quantity
    // Any THROW rolls back both Stock changes AND both movement rows.
    //
    // Returns the source + destination rows after the operation.
    Task<(Stock Source, Stock Destination)> TransferStockAsync(
        Guid fromStockId,
        Guid toLocationId,
        decimal quantity,
        StockMovementContext movementCtx,
        CancellationToken ct = default);

    // Phase 12 — snapshot read for cycle-count session creation.
    // Returns positive-OnHand rows whose Location belongs to the
    // given warehouse. When locationFilter is set, narrows further
    // to that single location. Sorted by Location.Code then
    // ProductId for stable session ordering.
    Task<IReadOnlyList<Stock>> GetPositiveOnHandByWarehouseAsync(
        Guid warehouseId,
        Guid? locationFilter,
        CancellationToken ct = default);
}
