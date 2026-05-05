using WMS.Domain.Entities.Inbound;

namespace WMS.DAL.Repositories.Inbound;

// Tenant-scoped CRUD-ish primitives for inbound.PurchaseOrders +
// PurchaseOrderLines. The repo handles the multi-table shape (one
// header, many lines) so the service doesn't have to coordinate
// transactions itself.
public interface IPurchaseOrderRepository
{
    // Inserts the header + lines in a single transaction. Both inputs'
    // Id properties must be pre-set by the caller (typically via
    // Guid.NewGuid()); the repo writes them as-is so the returned
    // header.Id matches what the caller already holds.
    //
    // Audit fields (CreatedAt default + CreatedBy = userId) are stamped
    // by the repo on every row.
    Task CreateAsync(
        PurchaseOrder header,
        IReadOnlyList<PurchaseOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default);

    // Header + lines in one round-trip via QueryMultiple — null when
    // the PO doesn't exist.
    Task<PurchaseOrderDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Same shape as GetByIdAsync but resolves by tenant-wide-unique
    // PoNumber. Convenience for scan-driven lookups (operator types
    // a PO number from a delivery note).
    Task<PurchaseOrderDetail?> GetByNumberAsync(string poNumber, CancellationToken ct = default);

    // Atomic ReceivedQuantity bump on a single line. Called per
    // receiving line that's linked to a PO line. The CHECK
    // CK_PurchaseOrderLines_ReceivedQty_NonNegative enforces the
    // invariant; the service is expected to pass a positive delta.
    // No version check today — receipts are append-only and serialise
    // naturally on the row's UPDATE lock.
    Task IncrementLineReceivedQuantityAsync(
        Guid poLineId,
        decimal delta,
        Guid? userId,
        CancellationToken ct = default);
}
