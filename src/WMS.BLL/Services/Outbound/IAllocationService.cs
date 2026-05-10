namespace WMS.BLL.Services.Outbound;

// Phase 14B (ADR-005) — outbound allocation orchestration. Wraps the
// strategy resolver + writes (OrderAllocations + Stock.Quantity-
// Allocated + line.AllocatedQuantity) inside one TransactionScope.
//
// State machine handled here:
//   Open       → Allocating | Allocated   (allocate, partial vs full)
//   Allocating → Allocating | Allocated   (re-allocate as stock arrives)
//   Allocated  → Allocated                (idempotent re-run)
//   Cancelled / Draft → throws            (wrong state)
//
// Reversal flow (cancel-after-allocate, future short-pick fail) lives
// in ReleaseAllForSalesOrderAsync. The orchestrating SalesOrderService
// .CancelAsync calls into here inside its TransactionScope.
public interface IAllocationService
{
    Task<AllocationResult> AllocateAsync(
        Guid tenantId,
        Guid salesOrderId,
        string? strategyName,
        Guid currentUserId,
        CancellationToken ct = default);

    // Releases every Active OrderAllocation on the SO's lines:
    //   1. UPDATE OrderAllocations SET Status='Released' (audit stamped)
    //   2. UPDATE Stock SET QuantityAllocated -= each freed qty
    //   3. UPDATE SalesOrderLines SET AllocatedQuantity -= each freed qty
    // Returns the count of allocations released (0 = SO had nothing
    // allocated, no-op). The caller owns the TransactionScope —
    // typically SalesOrderService.CancelAsync, where the scope also
    // wraps the matching SO header status flip.
    Task<int> ReleaseAllForSalesOrderAsync(
        Guid tenantId,
        Guid salesOrderId,
        string reason,
        Guid currentUserId,
        CancellationToken ct = default);
}

// Per-line shortfall summary returned by AllocateAsync. Allows the
// controller / UI to surface "lines short on stock" feedback without
// re-querying. IsFullyAllocated == ShortfallByLineId.Values.All(==0).
public sealed record AllocationResult(
    bool IsFullyAllocated,
    int LineCount,
    int FullyAllocatedLineCount,
    IReadOnlyDictionary<Guid, decimal> ShortfallByLineId);
