using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14C — pick task orchestration. T4 ships GenerateAsync only;
// SubmitAsync (T5) and CancelAsync (T6) plug onto this service.
//
// Generate is the lightest of the three lifecycle methods: no Stock
// writes, no allocation flips. Just inserts the task header + lines
// (snapshotting from Active OrderAllocations) and bumps the SO header
// status from Allocated → Picking. The TransactionScope is still
// required because the insert + status flip span two repos and must
// land atomically (already-Picking SOs would otherwise fall into a
// bad state on partial failure).
public sealed class PickTaskService : IPickTaskService
{
    private readonly ISalesOrderRepositoryFactory _soRepoFactory;
    private readonly IOrderAllocationRepositoryFactory _allocRepoFactory;
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly IPickTaskRepositoryFactory _pickRepoFactory;
    private readonly ILogger<PickTaskService> _logger;

    public PickTaskService(
        ISalesOrderRepositoryFactory soRepoFactory,
        IOrderAllocationRepositoryFactory allocRepoFactory,
        IStockRepositoryFactory stockRepoFactory,
        IPickTaskRepositoryFactory pickRepoFactory,
        ILogger<PickTaskService> logger)
    {
        _soRepoFactory = soRepoFactory;
        _allocRepoFactory = allocRepoFactory;
        _stockRepoFactory = stockRepoFactory;
        _pickRepoFactory = pickRepoFactory;
        _logger = logger;
    }

    public async Task<PickTaskGenerationResult> GenerateAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var soRepo = _soRepoFactory.For(tenantId);
        var pickRepo = _pickRepoFactory.For(tenantId);

        var detail = await soRepo.GetByIdAsync(salesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {salesOrderId} not found.");

        var status = detail.Header.Status;

        // Idempotent on Picking — return the existing Active task. The
        // controller can redirect to its Detail page rather than 500.
        if (status == "Picking")
        {
            var existing = await pickRepo.GetActiveBySalesOrderAsync(salesOrderId, ct)
                ?? throw new InvalidOperationException(
                    $"SO {salesOrderId} is in Picking state but no Active pick task exists.");
            var existingDetail = await pickRepo.GetByIdAsync(existing.Id, ct)
                ?? throw new InvalidOperationException(
                    $"Pick task {existing.Id} disappeared between lookups.");
            return new PickTaskGenerationResult(
                existing.Id,
                existing.PickNumber,
                existingDetail.Lines.Count,
                existingDetail.Lines.Sum(l => l.ExpectedQuantity));
        }

        if (status != "Allocated")
            throw new InvalidOperationException(
                $"Cannot generate pick task for SO in '{status}' state — only Allocated allowed.");

        // Defensive: even in Allocated state, refuse double-generation.
        var alreadyActive = await pickRepo.GetActiveBySalesOrderAsync(salesOrderId, ct);
        if (alreadyActive is not null)
            throw new InvalidOperationException(
                $"SO {salesOrderId} already has an Active pick task ({alreadyActive.PickNumber}).");

        var allocRepo = _allocRepoFactory.For(tenantId);
        var stockRepo = _stockRepoFactory.For(tenantId);

        var allocations = await allocRepo.GetActiveEntitiesBySalesOrderIdAsync(salesOrderId, ct);
        if (allocations.Count == 0)
            throw new InvalidOperationException(
                $"SO {salesOrderId} has no Active allocations to generate from.");

        // Snapshot Stock 6-tuple per allocation. Per-row read is fine
        // — allocation lists are small (one per (line, stock-row) pair).
        // No batch read API exists today; if pick generation grows past
        // ~50 allocations we can introduce IStockRepository.GetByIdsAsync.
        var lineNumber = 1;
        var lines = new List<PickTaskLine>(allocations.Count);
        var pickTaskId = Guid.NewGuid();

        foreach (var alloc in allocations)
        {
            var stock = await stockRepo.GetByIdAsync(alloc.StockId, ct)
                ?? throw new InvalidOperationException(
                    $"Stock row {alloc.StockId} referenced by allocation {alloc.Id} not found.");

            lines.Add(new PickTaskLine
            {
                Id = Guid.NewGuid(),
                PickTaskId = pickTaskId,
                LineNumber = lineNumber++,
                OrderAllocationId = alloc.Id,
                StockId = stock.Id,
                ProductId = stock.ProductId,
                OwnerId = stock.OwnerId,
                UomId = stock.UomId,
                LocationId = stock.LocationId,
                LotId = stock.LotId,
                PalletId = stock.PalletId,
                ExpectedQuantity = alloc.AllocatedQuantity,
                LineStatus = "Pending",
            });
        }

        var datePrefix = $"PICK-{DateTime.UtcNow:yyyyMMdd}-";
        var existingCount = await pickRepo.CountForDatePrefixAsync(datePrefix, ct);
        var pickNumber = $"{datePrefix}{(existingCount + 1):D4}";

        var header = new PickTask
        {
            Id = pickTaskId,
            PickNumber = pickNumber,
            SalesOrderId = salesOrderId,
            Status = "Pending",
            Notes = null,
        };

        // TX wraps the two-repo insert + SO status flip. Stock is
        // untouched here (allocation was the reservation; pick consumes
        // it later in T5's SubmitAsync). MSDTC promotion accepted per
        // `feedback_transactionscope_dapper.md`.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        await pickRepo.CreateAsync(header, lines, currentUserId, ct);

        var changed = await soRepo.SetStatusAsync(
            salesOrderId, "Allocated", "Picking", currentUserId, ct);
        if (!changed)
            throw new InvalidOperationException(
                $"Failed to flip SO {salesOrderId} status Allocated→Picking — concurrent state change?");

        scope.Complete();

        var totalExpected = lines.Sum(l => l.ExpectedQuantity);
        _logger.LogInformation(
            "Generated pick task {PickNumber} ({PickId}) for SO {SoNumber} ({SoId}) — {LineCount} lines, total expected {Total}",
            pickNumber, pickTaskId, detail.Header.SoNumber, salesOrderId, lines.Count, totalExpected);

        return new PickTaskGenerationResult(
            pickTaskId, pickNumber, lines.Count, totalExpected);
    }
}
