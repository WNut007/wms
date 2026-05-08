using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Tenant-scoped reader for inventory.StockMovements. Append-only —
// the WRITE side lives inside StockRepository's atomic SQL batches
// so the "movement INSERT shares a transaction with the Stock
// UPDATE" invariant from ADR-014 is enforceable. No mutation
// methods on this interface, by design.
public interface IStockMovementRepository
{
    // Per-Stock activity feed — uses IX_StockMovements_Stock seek.
    // Newest first. Default limit keeps the Activity panel cheap.
    Task<IReadOnlyList<StockMovement>> GetByStockAsync(
        Guid stockId, int limit = 50, CancellationToken ct = default);

    // Provenance lookup ("what movements came from receiving line X?"
    // / "show all movements tied to adjustment Y"). Uses the partial
    // IX_StockMovements_Reference index — Putaway's NULL ReferenceId
    // rows are out of this index and won't be returned (TD-004).
    Task<IReadOnlyList<StockMovement>> GetByReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken ct = default);

    // Per-product activity feed — JOINs inventory.Stock to filter by
    // ProductId, then LEFT JOINs security.Users (for PerformedByName)
    // and master.Locations twice (for From/To codes). Returns a
    // StockMovementListRow projection rather than the entity so the
    // resolved display fields don't pollute StockMovement itself.
    // Indexes Phase 6A built (IX_Stock_Product, IX_StockMovements_Stock)
    // still cover the leading lookup; the LEFT JOINs are seek-by-PK.
    Task<IReadOnlyList<StockMovementListRow>> GetByProductAsync(
        Guid productId,
        DateTime? since = null,
        int limit = 100,
        CancellationToken ct = default);
}
