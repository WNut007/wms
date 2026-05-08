using WMS.Domain.Entities.Inbound;

namespace WMS.DAL.Repositories.Inbound;

// Tenant-scoped persistence for the receiving aggregate. Mirrors
// IPurchaseOrderRepository's shape (transactional Create + two read
// flavours) plus a narrow update for the post-stock-creation Lot/Pallet
// link-back.
public interface IReceivingHeaderRepository
{
    // Inserts header + lines in a single transaction. Caller pre-sets
    // every Id (Guid.NewGuid()).
    Task CreateAsync(
        ReceivingHeader header,
        IReadOnlyList<ReceivingLine> lines,
        Guid? userId,
        CancellationToken ct = default);

    Task<ReceivingDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ReceivingDetail?> GetByNumberAsync(string receivingNumber, CancellationToken ct = default);

    // Stamps LotId / PalletId on a receiving line *after* the receiving
    // service has resolved them via β's lot/pallet upsert. Either Id
    // may be null (no lot tracking, no pallet); the UPDATE writes
    // exactly what's passed.
    Task UpdateLineInventoryRefsAsync(
        Guid lineId,
        Guid? lotId,
        Guid? palletId,
        Guid? userId,
        CancellationToken ct = default);

    // Per-warehouse activity feed for the Warehouse Detail Activity
    // tab. Lightweight projection: header-level fields + COALESCE'd
    // user-display name + COUNT(*) of lines. Index
    // IX_ReceivingHeaders_Warehouse(WarehouseId, ReceivedAt DESC) covers
    // the WHERE + ORDER BY exactly. Newest first; default cap matches
    // _ActivityPanel's "Last 30 days" copy without paging.
    Task<IReadOnlyList<ReceivingActivityRow>> GetActivityByWarehouseAsync(
        Guid warehouseId,
        int limit = 20,
        CancellationToken ct = default);
}
