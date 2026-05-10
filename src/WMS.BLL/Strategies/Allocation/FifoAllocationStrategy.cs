namespace WMS.BLL.Strategies.Allocation;

// Phase 14B — first-in-first-out allocation. Picks the oldest Stock
// rows (by CreatedAt) until the line's OutstandingQuantity is
// satisfied or candidates exhausted.
//
// "Oldest" is a proxy for "received earliest" — the receive flow
// stamps CreatedAt at INSERT time. Re-receiving the same key bumps
// QuantityOnHand on an existing row but doesn't change CreatedAt, so
// the strategy treats long-lived rows as "older" than fresh ones at
// the same warehouse/product. That's the conventional FIFO semantic.
//
// Stateless + deterministic — given the same input, same output.
public sealed class FifoAllocationStrategy : IAllocationStrategy
{
    public string Name => "FIFO";

    public AllocationDecision Allocate(AllocationContext context)
    {
        if (context.OutstandingQuantity <= 0m)
            return new AllocationDecision(Array.Empty<AllocationPick>(), 0m);

        // Repo already sorts by CreatedAt ASC; OrderBy here is defensive
        // (caller might pass a re-sorted list).
        var candidates = context.Candidates
            .OrderBy(s => s.CreatedAt)
            .ToList();

        var picks = new List<AllocationPick>();
        var remaining = context.OutstandingQuantity;

        foreach (var stock in candidates)
        {
            if (remaining <= 0m) break;

            var available = stock.QuantityOnHand - stock.QuantityAllocated;
            if (available <= 0m) continue; // defensive — repo filters this out

            var take = Math.Min(available, remaining);
            picks.Add(new AllocationPick(stock.Id, take));
            remaining -= take;
        }

        return new AllocationDecision(picks, remaining);
    }
}
