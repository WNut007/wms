using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14B — tenant-scoped persistence for outbound.OrderAllocations.
// Ambient-TX-aware on writes (orchestrator owns the transaction during
// AllocateAsync / CancelAsync). Reads are JOIN-resolved for direct
// panel rendering.
public interface IOrderAllocationRepository
{
    // INSERT a single allocation. Caller pre-assigns Id. Status defaults
    // to 'Active' via DB. AllocatedAt populates server-side.
    Task CreateAsync(
        OrderAllocation allocation, Guid? userId, CancellationToken ct = default);

    // Batch INSERT — used when one SO line spans multiple Stock rows
    // (FIFO across lots). All-or-nothing inside the caller's TX.
    Task CreateBatchAsync(
        IReadOnlyList<OrderAllocation> allocations,
        Guid? userId,
        CancellationToken ct = default);

    // Detail-panel reads. Active-only by default — Released allocations
    // are audit history; the panel shows what's currently reserving
    // stock for the line / SO.
    Task<IReadOnlyList<OrderAllocationRow>> GetActiveByLineIdAsync(
        Guid salesOrderLineId, CancellationToken ct = default);

    Task<IReadOnlyList<OrderAllocationRow>> GetActiveBySalesOrderIdAsync(
        Guid salesOrderId, CancellationToken ct = default);

    // Atomic flip Active → Released with audit. Returns true when a
    // row was changed (false on already-Released — idempotent).
    Task<bool> ReleaseAsync(
        Guid allocationId,
        string? reason,
        Guid? userId,
        CancellationToken ct = default);

    // Bulk release for cancel-after-allocate flow. Flips every Active
    // allocation across the SO's lines to Released and returns the
    // count of rows changed. Service layer composes this with the
    // matching Stock.QuantityAllocated decrements + line.AllocatedQty
    // resets in one TX.
    Task<int> ReleaseAllForSalesOrderAsync(
        Guid salesOrderId,
        string reason,
        Guid? userId,
        CancellationToken ct = default);

    // Read all Active allocations across an SO — distinct from
    // GetActiveBySalesOrderIdAsync's display projection. Returns the
    // raw entities so the cancel-reversal flow can iterate and apply
    // matching Stock decrements per (StockId, qty) tuple.
    Task<IReadOnlyList<OrderAllocation>> GetActiveEntitiesBySalesOrderIdAsync(
        Guid salesOrderId, CancellationToken ct = default);

    // Phase 14C — atomic Active → Picked flip with audit. Called by
    // PickTaskService.SubmitAsync inside ambient TransactionScope when
    // a pick task consumes the allocation. Returns true when changed.
    Task<bool> MarkPickedAsync(
        Guid allocationId,
        Guid? userId,
        CancellationToken ct = default);
}
