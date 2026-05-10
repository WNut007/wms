using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Strategies.Allocation;

// Phase 14B (ADR-005) — pluggable allocation strategy. Given a SO
// line's outstanding quantity + a list of candidate Stock rows
// (already filtered by Warehouse + Product + Owner + UoM with
// Available > 0), the strategy decides which rows to pick from and
// how much from each.
//
// Strategies are stateless and pure — same input → same output. The
// orchestrating AllocationService writes the resulting Decision to
// OrderAllocations / Stock.QuantityAllocated / SalesOrderLine.
// AllocatedQuantity inside one TransactionScope.
//
// MVP ships only FifoAllocationStrategy. FEFO + tier-aware + ATP
// strategies plug in later via the resolver — no AllocationService
// change needed.
//
// Per CLAUDE.md "no hardcoded strategies" — service code never
// instantiates this directly; it goes through IAllocationStrategy-
// Resolver.
public interface IAllocationStrategy
{
    // Stable identifier for the resolver to look up. Conventional
    // PascalCase short name (e.g. "FIFO", "FEFO", "Tier", "ATP").
    string Name { get; }

    AllocationDecision Allocate(AllocationContext context);
}

// Per-line input. OutstandingQuantity = OrderedQty - already-Allocated
// (recomputing from the line entity is fine; the AllocationService
// passes it pre-computed for clarity). Candidates already filtered +
// FIFO-sorted by the repo; strategies may re-order in memory.
public sealed record AllocationContext(
    Guid SalesOrderLineId,
    decimal OutstandingQuantity,
    IReadOnlyList<Stock> Candidates);

// Per-line output. Picks sum to ≤ OutstandingQuantity. Shortfall =
// OutstandingQuantity - sum(Picks) ≥ 0. Empty Picks + Shortfall =
// OutstandingQuantity is a valid "nothing available" outcome — service
// flips header to Allocating (vs Allocated when every line has zero
// shortfall).
public sealed record AllocationDecision(
    IReadOnlyList<AllocationPick> Picks,
    decimal Shortfall);

public sealed record AllocationPick(
    Guid StockId,
    decimal Quantity);
