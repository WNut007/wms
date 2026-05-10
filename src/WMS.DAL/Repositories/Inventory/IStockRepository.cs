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

    // Phase 14B — candidate Stock rows for outbound allocation.
    // Filters by warehouse + 3-tuple (Product, Owner, UoM) — Lot is
    // not a filter today (SO lines don't specify lot in MVP; future
    // FEFO strategy can sort candidates by lot expiry). Returns rows
    // where (QuantityOnHand - QuantityAllocated) > 0. Sorted by
    // CreatedAt ASC (FIFO-friendly default; strategy may re-order in
    // memory). Atomic concurrent allocation safety relies on the
    // CK_Stock_Allocated_NotOverOnHand constraint at update time.
    Task<IReadOnlyList<Stock>> GetAllocationCandidatesAsync(
        Guid warehouseId,
        Guid productId,
        Guid ownerId,
        Guid uomId,
        CancellationToken ct = default);

    // Phase 14B — atomic delta on QuantityAllocated. Positive on
    // allocate, negative on release. Caller composes inside ambient
    // TransactionScope alongside the matching OrderAllocations row +
    // line.AllocatedQuantity bump. CK_Stock_Allocated_NotOverOnHand +
    // CK_Stock_Allocated_NonNegative throw on out-of-range deltas —
    // service catches InvalidOperationException, TX rolls back.
    Task AdjustQuantityAllocatedAsync(
        Guid stockId,
        decimal delta,
        Guid? userId,
        CancellationToken ct = default);

    // Phase 20 — mobile Putaway queue read. Returns Stock rows in the
    // given warehouse that sit at a Receiving / Staging-zone location
    // with positive OnHand. Sorted CreatedAt ASC (FIFO oldest first —
    // operator clears the backlog). One JOIN-rich projection per row
    // covers the per-card render without per-row lookups.
    Task<IReadOnlyList<PutawayQueueRow>> GetPutawayQueueAsync(
        Guid warehouseId,
        CancellationToken ct = default);

    // Phase 20 — suggested putaway destination for a product in the
    // given warehouse. Filters to Storage-zone locations only
    // (IsActive=1, Status='Active'). Scoring (descending priority):
    //   1. SameProductRowCount DESC — cluster picks (existing same-
    //      product Stock at the location; raises pick-face hit rate)
    //   2. BinRank ASC — BC-style "lower rank fills first"
    //   3. IsPickface ASC — preserve dedicated pick faces for pulls
    // Returns null when no Storage-zone location qualifies. Capacity-
    // aware scoring deferred (TD — needs product-volume data).
    Task<SuggestedLocationResult?> GetSuggestedPutawayLocationAsync(
        Guid warehouseId,
        Guid productId,
        CancellationToken ct = default);
}
