using WMS.DAL.Common;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Phase 13 (ADR-012) — tenant-scoped persistence for TransferOrder
// header + lines. State transitions are atomic single-statement
// UPDATEs so the service can compose them inside a TransactionScope
// alongside Stock writes (Phase 10B/11A/12 pattern).
public interface ITransferOrderRepository
{
    // Inserts header + lines in a single transaction. Caller assigns
    // every Id + TransferNumber.
    Task CreateAsync(
        TransferOrder header,
        IReadOnlyList<TransferOrderLine> lines,
        Guid requestedBy,
        CancellationToken ct = default);

    Task<TransferOrderDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TransferOrderDetail?> GetByNumberAsync(string transferNumber, CancellationToken ct = default);

    // Resolved-row projection for the Detail Lines tab.
    Task<IReadOnlyList<TransferOrderLineRow>> GetLineRowsByIdAsync(
        Guid transferId, CancellationToken ct = default);

    Task<PagedResult<TransferOrderListRow>> GetPagedAsync(
        TransferOrderFilter filter, CancellationToken ct = default);

    Task<TransferOrderStatusCounts> GetStatusCountsAsync(
        TransferOrderFilter filter, CancellationToken ct = default);

    // ----- State transitions (header) -----
    // All idempotent at SQL level via WHERE Status=@from filter.

    Task<bool> SetSubmittedAsync(
        Guid transferId, Guid submittedBy, CancellationToken ct = default);

    Task<bool> SetApprovedAsync(
        Guid transferId, Guid approvedBy, CancellationToken ct = default);

    Task<bool> SetInTransitAsync(
        Guid transferId, Guid dispatchedBy, CancellationToken ct = default);

    Task<bool> SetReceivedAsync(
        Guid transferId, Guid receivedBy, CancellationToken ct = default);

    // Cancel from any pre-InTransit state. Caller passes the from
    // state for atomicity (idempotent on already-Cancelled).
    Task<bool> SetCancelledAsync(
        Guid transferId, string fromStatus, string reason,
        Guid cancelledBy, CancellationToken ct = default);

    // Lost from InTransit only.
    Task<bool> SetLostAsync(
        Guid transferId, string reason, Guid lostBy,
        CancellationToken ct = default);

    // ----- Per-line updates -----

    // Dispatch sets QtyDispatched + flips line.Status to 'Dispatched'.
    // Service composes this with Stock decrement inside one TX.
    Task UpdateLineDispatchedAsync(
        Guid lineId, decimal qtyDispatched, Guid currentUserId,
        CancellationToken ct = default);

    // Receive sets QtyReceived. Status flips to 'Received' when the
    // received qty matches dispatched, or 'Variance' when short.
    // Service computes the target status before calling.
    Task UpdateLineReceivedAsync(
        Guid lineId, decimal qtyReceived, string targetStatus,
        Guid currentUserId, CancellationToken ct = default);

    // CountForDatePrefixAsync — TransferNumber assignment.
    Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default);
}

public interface ITransferOrderRepositoryFactory
{
    ITransferOrderRepository For(Guid tenantId);
}
