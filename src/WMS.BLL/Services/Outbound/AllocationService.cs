using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.BLL.Strategies.Allocation;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14B — orchestrates allocation across an SO's lines.
// TransactionScope-wrapped per Phase 11A/12/13 precedent — MSDTC
// trade-off accepted (multi-connection, multi-repo). Insufficient
// stock during allocation throws via CK_Stock_Allocated_NotOverOnHand
// (rare — repo's GetAllocationCandidates already filters); TX rolls
// back cleanly.
public sealed class AllocationService : IAllocationService
{
    private readonly ISalesOrderRepositoryFactory _soRepoFactory;
    private readonly IOrderAllocationRepositoryFactory _allocRepoFactory;
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly IAllocationStrategyResolver _strategyResolver;
    private readonly ILogger<AllocationService> _logger;

    public AllocationService(
        ISalesOrderRepositoryFactory soRepoFactory,
        IOrderAllocationRepositoryFactory allocRepoFactory,
        IStockRepositoryFactory stockRepoFactory,
        IAllocationStrategyResolver strategyResolver,
        ILogger<AllocationService> logger)
    {
        _soRepoFactory = soRepoFactory;
        _allocRepoFactory = allocRepoFactory;
        _stockRepoFactory = stockRepoFactory;
        _strategyResolver = strategyResolver;
        _logger = logger;
    }

    public async Task<AllocationResult> AllocateAsync(
        Guid tenantId,
        Guid salesOrderId,
        string? strategyName,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var soRepo = _soRepoFactory.For(tenantId);
        var detail = await soRepo.GetByIdAsync(salesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {salesOrderId} not found.");

        var status = detail.Header.Status;
        if (status == "Allocated")
        {
            // Idempotent — caller may re-trigger; nothing to do.
            return BuildResult(detail.Lines, Array.Empty<(Guid, decimal)>());
        }
        if (status is not "Open" and not "Allocating")
            throw new InvalidOperationException(
                $"Cannot allocate SO in '{status}' state — only Open or Allocating allowed.");

        var strategy = _strategyResolver.Resolve(strategyName);
        var stockRepo = _stockRepoFactory.For(tenantId);
        var allocRepo = _allocRepoFactory.For(tenantId);

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        // Per-line allocation accumulator. Recorded in this loop so the
        // result can surface line-level shortfall without a re-query.
        var newAllocByLine = new List<(Guid LineId, decimal AddedQty)>();

        foreach (var line in detail.Lines)
        {
            var outstanding = line.OrderedQuantity - line.AllocatedQuantity;
            if (outstanding <= 0m) continue; // already fully allocated

            var candidates = await stockRepo.GetAllocationCandidatesAsync(
                detail.Header.WarehouseId,
                line.ProductId,
                line.OwnerId,
                line.UomId,
                ct);

            var decision = strategy.Allocate(new AllocationContext(
                line.Id, outstanding, candidates));

            if (decision.Picks.Count == 0)
            {
                // No-op for this line — full shortfall recorded later
                // by recomputing from line.AllocatedQuantity vs Ordered.
                continue;
            }

            // Apply the picks: write OrderAllocation + bump Stock +
            // accumulate the line delta (commit per-line at the end so
            // the line UPDATE is one statement).
            decimal addedThisLine = 0m;
            foreach (var pick in decision.Picks)
            {
                var allocation = new OrderAllocation
                {
                    Id = Guid.NewGuid(),
                    SalesOrderLineId = line.Id,
                    StockId = pick.StockId,
                    AllocatedQuantity = pick.Quantity,
                    Status = "Active",
                };
                await allocRepo.CreateAsync(allocation, currentUserId, ct);
                await stockRepo.AdjustQuantityAllocatedAsync(
                    pick.StockId, pick.Quantity, currentUserId, ct);
                addedThisLine += pick.Quantity;
            }

            await soRepo.AdjustLineAllocatedQuantityAsync(
                line.Id, addedThisLine, currentUserId, ct);

            newAllocByLine.Add((line.Id, addedThisLine));
        }

        // Decide target header status. Recompute outstanding using the
        // initial line state + what we just allocated; one fresh read
        // of the full lines list would also work but adds a round-trip.
        var perLineShortfall = detail.Lines.ToDictionary(
            l => l.Id,
            l =>
            {
                var added = newAllocByLine
                    .FirstOrDefault(t => t.LineId == l.Id).AddedQty;
                var newTotalAllocated = l.AllocatedQuantity + added;
                var shortfall = l.OrderedQuantity - newTotalAllocated;
                return shortfall > 0m ? shortfall : 0m;
            });

        var totalShortfall = perLineShortfall.Values.Sum();
        var targetStatus = totalShortfall <= 0m ? "Allocated" : "Allocating";

        // Try Open → target, then Allocating → target. Whichever applies
        // hits; the other is a no-op via WHERE Status=@from filter.
        var changed =
               await soRepo.SetStatusAsync(salesOrderId, "Open",       targetStatus, currentUserId, ct)
            || await soRepo.SetStatusAsync(salesOrderId, "Allocating", targetStatus, currentUserId, ct);

        if (!changed)
            throw new InvalidOperationException(
                $"Failed to flip SO {salesOrderId} status to '{targetStatus}' — concurrent state change?");

        scope.Complete();

        var result = BuildResult(detail.Lines, newAllocByLine);
        _logger.LogInformation(
            "Allocated SO {SoNumber} ({SoId}) — strategy={Strategy} status={Status} fully={Full} shortfall={Shortfall}",
            detail.Header.SoNumber, salesOrderId, strategy.Name, targetStatus,
            result.IsFullyAllocated, totalShortfall);

        return result;
    }

    public async Task<int> ReleaseAllForSalesOrderAsync(
        Guid tenantId,
        Guid salesOrderId,
        string reason,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Release reason is required.", nameof(reason));

        var allocRepo = _allocRepoFactory.For(tenantId);
        var stockRepo = _stockRepoFactory.For(tenantId);
        var soRepo = _soRepoFactory.For(tenantId);

        // Caller owns the TransactionScope (SalesOrderService.CancelAsync
        // typically). All four steps below land or none.
        var activeEntities = await allocRepo.GetActiveEntitiesBySalesOrderIdAsync(
            salesOrderId, ct);
        if (activeEntities.Count == 0) return 0;

        // Step 1: Decrement Stock.QuantityAllocated per allocation.
        foreach (var alloc in activeEntities)
        {
            await stockRepo.AdjustQuantityAllocatedAsync(
                alloc.StockId, -alloc.AllocatedQuantity, currentUserId, ct);
        }

        // Step 2: Decrement SalesOrderLines.AllocatedQuantity per line
        // (sum freed qty across that line's allocations).
        var freedByLine = activeEntities
            .GroupBy(a => a.SalesOrderLineId)
            .Select(g => (LineId: g.Key, Total: g.Sum(a => a.AllocatedQuantity)));
        foreach (var (lineId, total) in freedByLine)
        {
            await soRepo.AdjustLineAllocatedQuantityAsync(
                lineId, -total, currentUserId, ct);
        }

        // Step 3: Bulk-flip Active → Released with audit on every row.
        var released = await allocRepo.ReleaseAllForSalesOrderAsync(
            salesOrderId, reason.Trim(), currentUserId, ct);

        _logger.LogInformation(
            "Released {Count} allocation(s) for SO {SoId} — reason: {Reason}",
            released, salesOrderId, reason);

        return released;
    }

    private static AllocationResult BuildResult(
        IReadOnlyList<SalesOrderLine> linesBefore,
        IReadOnlyList<(Guid LineId, decimal AddedQty)> additions)
    {
        var byLine = additions.ToDictionary(t => t.LineId, t => t.AddedQty);

        var shortfallByLineId = linesBefore.ToDictionary(
            l => l.Id,
            l =>
            {
                var added = byLine.GetValueOrDefault(l.Id, 0m);
                var newTotal = l.AllocatedQuantity + added;
                var shortfall = l.OrderedQuantity - newTotal;
                return shortfall > 0m ? shortfall : 0m;
            });

        var fullyAllocated = shortfallByLineId.Values.Count(s => s == 0m);

        return new AllocationResult(
            IsFullyAllocated: shortfallByLineId.Values.All(s => s == 0m),
            LineCount: linesBefore.Count,
            FullyAllocatedLineCount: fullyAllocated,
            ShortfallByLineId: shortfallByLineId);
    }
}
