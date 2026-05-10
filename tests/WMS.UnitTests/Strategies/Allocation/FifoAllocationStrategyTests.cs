using WMS.BLL.Strategies.Allocation;
using WMS.Domain.Entities.Inventory;

namespace WMS.UnitTests.Strategies.Allocation;

// Phase 14B (ADR-005) — pure-function strategy tests. No Moq, no DI.
public class FifoAllocationStrategyTests
{
    private static readonly Guid LineId = Guid.NewGuid();

    private static Stock NewStock(decimal onHand, decimal allocated, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        QuantityOnHand = onHand,
        QuantityAllocated = allocated,
        CreatedAt = createdAt,
    };

    [Fact]
    public void EmptyCandidates_ReturnsEmptyPicks_FullShortfall()
    {
        var sut = new FifoAllocationStrategy();
        var ctx = new AllocationContext(LineId, OutstandingQuantity: 5m, Candidates: Array.Empty<Stock>());

        var decision = sut.Allocate(ctx);

        Assert.Empty(decision.Picks);
        Assert.Equal(5m, decision.Shortfall);
    }

    [Fact]
    public void ZeroOutstanding_ReturnsEmptyPicks_ZeroShortfall()
    {
        var sut = new FifoAllocationStrategy();
        var ctx = new AllocationContext(
            LineId, OutstandingQuantity: 0m,
            Candidates: new[] { NewStock(100m, 0m, DateTime.UtcNow) });

        var decision = sut.Allocate(ctx);

        Assert.Empty(decision.Picks);
        Assert.Equal(0m, decision.Shortfall);
    }

    [Fact]
    public void SingleCandidateCoversFullRequest_OnePick_NoShortfall()
    {
        var sut = new FifoAllocationStrategy();
        var stock = NewStock(onHand: 100m, allocated: 0m, createdAt: DateTime.UtcNow);
        var ctx = new AllocationContext(LineId, 30m, new[] { stock });

        var decision = sut.Allocate(ctx);

        Assert.Single(decision.Picks);
        Assert.Equal(stock.Id, decision.Picks[0].StockId);
        Assert.Equal(30m, decision.Picks[0].Quantity);
        Assert.Equal(0m, decision.Shortfall);
    }

    [Fact]
    public void SingleCandidateInsufficient_OnePickAtAvailable_PartialShortfall()
    {
        var sut = new FifoAllocationStrategy();
        var stock = NewStock(onHand: 100m, allocated: 95m, createdAt: DateTime.UtcNow); // 5 available
        var ctx = new AllocationContext(LineId, 30m, new[] { stock });

        var decision = sut.Allocate(ctx);

        Assert.Single(decision.Picks);
        Assert.Equal(5m, decision.Picks[0].Quantity);
        Assert.Equal(25m, decision.Shortfall);
    }

    [Fact]
    public void MultipleCandidates_FillsAcross_FifoOrder()
    {
        var sut = new FifoAllocationStrategy();
        var older  = NewStock(50m, 0m, DateTime.UtcNow.AddDays(-7));
        var newer  = NewStock(50m, 0m, DateTime.UtcNow);
        // Pass in non-FIFO order to verify the strategy sorts.
        var ctx = new AllocationContext(LineId, 80m, new[] { newer, older });

        var decision = sut.Allocate(ctx);

        Assert.Equal(2, decision.Picks.Count);
        // Older first per FIFO.
        Assert.Equal(older.Id, decision.Picks[0].StockId);
        Assert.Equal(50m, decision.Picks[0].Quantity);
        Assert.Equal(newer.Id, decision.Picks[1].StockId);
        Assert.Equal(30m, decision.Picks[1].Quantity);
        Assert.Equal(0m, decision.Shortfall);
    }

    [Fact]
    public void StopsTakingOnceFilled_DoesntPickFromLaterCandidates()
    {
        var sut = new FifoAllocationStrategy();
        var first  = NewStock(40m, 0m, DateTime.UtcNow.AddDays(-2));
        var second = NewStock(40m, 0m, DateTime.UtcNow.AddDays(-1));
        var third  = NewStock(40m, 0m, DateTime.UtcNow);
        var ctx = new AllocationContext(LineId, 50m, new[] { first, second, third });

        var decision = sut.Allocate(ctx);

        // first fully (40), second partially (10), third untouched.
        Assert.Equal(2, decision.Picks.Count);
        Assert.Equal(40m, decision.Picks[0].Quantity);
        Assert.Equal(10m, decision.Picks[1].Quantity);
        Assert.Equal(0m, decision.Shortfall);
    }

    [Fact]
    public void SkipsFullyAllocatedRow_DefensiveAgainstUnfilteredInput()
    {
        var sut = new FifoAllocationStrategy();
        // Repo's GetAllocationCandidatesAsync filters this row out, but
        // strategy should be defensive — the contract says "available > 0"
        // not "available exists".
        var saturated = NewStock(onHand: 50m, allocated: 50m, createdAt: DateTime.UtcNow.AddDays(-5));
        var open      = NewStock(onHand: 50m, allocated: 0m,  createdAt: DateTime.UtcNow);
        var ctx = new AllocationContext(LineId, 30m, new[] { saturated, open });

        var decision = sut.Allocate(ctx);

        Assert.Single(decision.Picks);
        Assert.Equal(open.Id, decision.Picks[0].StockId);
        Assert.Equal(30m, decision.Picks[0].Quantity);
    }

    [Fact]
    public void NameIsFifo()
    {
        Assert.Equal("FIFO", new FifoAllocationStrategy().Name);
    }
}
