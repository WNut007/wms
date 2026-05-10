using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14C — pick task orchestration. T4 ships GenerateAsync; T5
// adds SubmitAsync (the headline TX-wrapped commit). CancelAsync
// arrives in T6.
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

    public async Task<PickTaskSubmissionResult> SubmitAsync(
        Guid tenantId,
        SubmitPickTaskRequest request,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pickRepo = _pickRepoFactory.For(tenantId);
        var allocRepo = _allocRepoFactory.For(tenantId);
        var stockRepo = _stockRepoFactory.For(tenantId);
        var soRepo = _soRepoFactory.For(tenantId);

        var detail = await pickRepo.GetByIdAsync(request.PickTaskId, ct)
            ?? throw new InvalidOperationException(
                $"PickTask {request.PickTaskId} not found.");

        var taskStatus = detail.Header.Status;
        if (taskStatus is not "Pending" and not "InProgress")
            throw new InvalidOperationException(
                $"Cannot submit pick task in '{taskStatus}' state — only Pending or InProgress allowed.");

        var entriesByLineId = ValidateRequestShape(request, detail.Lines);

        // Resolve SalesOrderLineId per allocation (PickTaskLine doesn't
        // carry it — snapshot intentionally narrow). One read covers
        // every allocation on the SO.
        var allocations = await allocRepo.GetActiveEntitiesBySalesOrderIdAsync(
            detail.Header.SalesOrderId, ct);
        var allocById = allocations.ToDictionary(a => a.Id);

        // Per-SO-line accumulators for the post-pick aggregate (used to
        // compute SO target status without re-reading SO lines).
        var pickedBySoLine = new Dictionary<Guid, decimal>();
        var releasedBySoLine = new Dictionary<Guid, decimal>();

        int fullyPickedLines = 0;
        int shortPickedLines = 0;
        int skippedLines = 0;
        decimal totalPicked = 0m;

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        foreach (var line in detail.Lines)
        {
            var entry = entriesByLineId[line.Id];

            // ValidateRequestShape already enforced the per-line input
            // contract — pickedQty derived directly off the validated
            // entry shape.
            var pickedQty = entry.LineStatus == "Picked"
                ? entry.PickedQuantity!.Value
                : 0m;

            if (entry.LineStatus == "Skipped")
                skippedLines++;
            else if (pickedQty < line.ExpectedQuantity)
                shortPickedLines++;
            else
                fullyPickedLines++;

            totalPicked += pickedQty;

            // 1. PickTaskLine update — qty + status + reason + notes.
            await pickRepo.UpdateLinePickedAsync(
                line.Id,
                entry.LineStatus == "Picked" ? entry.PickedQuantity : null,
                entry.LineStatus,
                entry.ShortPickReason,
                entry.Notes,
                currentUserId,
                ct);

            // 2. Stock.OnHand decrement — only when something was
            // actually picked. CK_Stock_OnHand_NonNegative throws on
            // concurrent drain → TX rolls back.
            if (pickedQty > 0m)
            {
                var stockKey = new StockKey(
                    line.LocationId,
                    line.ProductId,
                    line.LotId,
                    line.PalletId,
                    line.OwnerId,
                    line.UomId);

                var movementCtx = new StockMovementContext(
                    StockMovementType.Pick,
                    currentUserId,
                    ReferenceType: "PickTaskLine",
                    ReferenceId: line.Id,
                    Notes: $"Pick {detail.Header.PickNumber}");

                await stockRepo.UpsertOnHandAsync(stockKey, -pickedQty, movementCtx, ct);
            }

            // 3. Stock.QuantityAllocated decrement — release the FULL
            // expected reservation. The picked portion left via OnHand
            // above; the unfilled portion is now free for re-allocation.
            await stockRepo.AdjustQuantityAllocatedAsync(
                line.StockId, -line.ExpectedQuantity, currentUserId, ct);

            // 4. OrderAllocation Active → Picked.
            if (!allocById.TryGetValue(line.OrderAllocationId, out var alloc))
                throw new InvalidOperationException(
                    $"OrderAllocation {line.OrderAllocationId} for pick line {line.Id} not Active — concurrent state change?");

            var pickedFlipped = await allocRepo.MarkPickedAsync(
                alloc.Id, currentUserId, ct);
            if (!pickedFlipped)
                throw new InvalidOperationException(
                    $"OrderAllocation {alloc.Id} no longer Active — concurrent cancel or pick?");

            // Per-SO-line bookkeeping (one allocation may share an SO
            // line with siblings — sum on the way through).
            pickedBySoLine[alloc.SalesOrderLineId] =
                pickedBySoLine.GetValueOrDefault(alloc.SalesOrderLineId, 0m) + pickedQty;
            releasedBySoLine[alloc.SalesOrderLineId] =
                releasedBySoLine.GetValueOrDefault(alloc.SalesOrderLineId, 0m) + line.ExpectedQuantity;
        }

        // 5. SO line aggregates — one UPDATE per affected line for both
        // PickedQuantity bump + AllocatedQuantity decrement.
        foreach (var (soLineId, pickedDelta) in pickedBySoLine)
        {
            if (pickedDelta > 0m)
                await soRepo.AdjustLinePickedQuantityAsync(
                    soLineId, pickedDelta, currentUserId, ct);
        }
        foreach (var (soLineId, releasedDelta) in releasedBySoLine)
        {
            await soRepo.AdjustLineAllocatedQuantityAsync(
                soLineId, -releasedDelta, currentUserId, ct);
        }

        // 6. Task header — Pending→InProgress→Completed in one
        // round-trip pair. SetStartedAsync no-ops when already
        // InProgress (idempotent via WHERE Status='Pending').
        if (taskStatus == "Pending")
        {
            var started = await pickRepo.SetStartedAsync(
                request.PickTaskId, currentUserId, ct);
            if (!started)
                throw new InvalidOperationException(
                    $"Failed to advance pick task {request.PickTaskId} to InProgress — concurrent state change?");
        }

        // Target status: any short or skipped line → PartiallyPicked,
        // else Picked.
        var targetTaskStatus = (shortPickedLines + skippedLines) > 0
            ? "PartiallyPicked"
            : "Picked";

        var completed = await pickRepo.SetCompletedAsync(
            request.PickTaskId, targetTaskStatus, currentUserId, ct);
        if (!completed)
            throw new InvalidOperationException(
                $"Failed to flip pick task {request.PickTaskId} to '{targetTaskStatus}' — concurrent state change?");

        // 7. SO header status. Re-read lines to capture the post-pick
        // PickedQuantity per line (denormalized aggregate). MVP:
        // one pick task per SO, so PickedQuantity == pickedBySoLine
        // delta for every line — but a fresh read keeps the logic
        // robust against future multi-task SOs without rewriting.
        var soDetail = await soRepo.GetByIdAsync(detail.Header.SalesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SO {detail.Header.SalesOrderId} disappeared mid-submit.");

        var allLinesFullyPicked = soDetail.Lines.All(l =>
            l.PickedQuantity >= l.OrderedQuantity);
        var targetSoStatus = allLinesFullyPicked ? "Picked" : "PartiallyPicked";

        var soChanged = await soRepo.SetStatusAsync(
            detail.Header.SalesOrderId, "Picking", targetSoStatus, currentUserId, ct);
        if (!soChanged)
            throw new InvalidOperationException(
                $"Failed to flip SO {detail.Header.SalesOrderId} Picking→'{targetSoStatus}' — concurrent state change?");

        scope.Complete();

        _logger.LogInformation(
            "Submitted pick task {PickNumber} ({PickId}) — task={TaskStatus} so={SoStatus} full={Full} short={Short} skip={Skip} totalPicked={Total}",
            detail.Header.PickNumber, request.PickTaskId,
            targetTaskStatus, targetSoStatus,
            fullyPickedLines, shortPickedLines, skippedLines, totalPicked);

        return new PickTaskSubmissionResult(
            TaskStatus: targetTaskStatus,
            SalesOrderStatus: targetSoStatus,
            FullyPickedLineCount: fullyPickedLines,
            ShortPickedLineCount: shortPickedLines,
            SkippedLineCount: skippedLines,
            TotalPickedQuantity: totalPicked);
    }

    public async Task<bool> CancelAsync(
        Guid tenantId,
        Guid pickTaskId,
        string reason,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancel reason is required.", nameof(reason));

        var pickRepo = _pickRepoFactory.For(tenantId);
        var soRepo = _soRepoFactory.For(tenantId);

        var detail = await pickRepo.GetByIdAsync(pickTaskId, ct)
            ?? throw new InvalidOperationException(
                $"PickTask {pickTaskId} not found.");

        var fromStatus = detail.Header.Status;

        // Idempotent on already-Cancelled — caller may double-click.
        if (fromStatus == "Cancelled") return false;

        // Submit-terminal states are point-of-no-return — Stock + SO
        // line + allocation rows have all been mutated; reversing would
        // need a "return to stock" workflow (out of scope).
        if (fromStatus is "Picked" or "PartiallyPicked")
            throw new InvalidOperationException(
                $"Cannot cancel pick task in '{fromStatus}' state — task is already submitted. " +
                "Use a return-to-stock flow to reverse a posted pick (not yet implemented).");

        if (fromStatus is not "Pending" and not "InProgress")
            throw new InvalidOperationException(
                $"Cannot cancel pick task in '{fromStatus}' state.");

        var trimmedReason = reason.Trim();

        // TX wraps the two-repo flip. Generate didn't touch Stock or
        // OrderAllocations, so neither does Cancel — allocations stay
        // Active, the SO returns to Allocated, ready for a re-Generate
        // by another picker if needed.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        var taskFlipped = await pickRepo.SetCancelledAsync(
            pickTaskId, fromStatus, trimmedReason, currentUserId, ct);
        if (!taskFlipped)
            throw new InvalidOperationException(
                $"Failed to cancel pick task {pickTaskId} from '{fromStatus}' — concurrent state change?");

        var soChanged = await soRepo.SetStatusAsync(
            detail.Header.SalesOrderId, "Picking", "Allocated", currentUserId, ct);
        if (!soChanged)
            throw new InvalidOperationException(
                $"Failed to flip SO {detail.Header.SalesOrderId} Picking→Allocated — concurrent state change?");

        scope.Complete();

        _logger.LogInformation(
            "Cancelled pick task {PickNumber} ({PickId}) from '{FromStatus}' — reason: {Reason}",
            detail.Header.PickNumber, pickTaskId, fromStatus, trimmedReason);

        return true;
    }

    // Verifies the request covers exactly the task's lines (no missing,
    // no extras, no duplicates) and each entry's per-line shape is
    // valid. Returns LineId → entry lookup for the orchestrating loop.
    private static Dictionary<Guid, PickedLineEntry> ValidateRequestShape(
        SubmitPickTaskRequest request,
        IReadOnlyList<PickTaskLine> taskLines)
    {
        if (request.Lines.Count == 0)
            throw new InvalidOperationException("Submission has no lines.");

        var dict = new Dictionary<Guid, PickedLineEntry>(request.Lines.Count);
        foreach (var entry in request.Lines)
        {
            if (!dict.TryAdd(entry.LineId, entry))
                throw new InvalidOperationException(
                    $"Duplicate LineId {entry.LineId} in submission.");
        }

        var taskLineIds = taskLines.Select(l => l.Id).ToHashSet();
        var missing = taskLineIds.Except(dict.Keys).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Submission missing lines: {string.Join(", ", missing)}.");
        var extras = dict.Keys.Except(taskLineIds).ToList();
        if (extras.Count > 0)
            throw new InvalidOperationException(
                $"Submission has unknown lines: {string.Join(", ", extras)}.");

        // Per-entry shape — closed enum, qty range, reason required.
        foreach (var line in taskLines)
        {
            var entry = dict[line.Id];

            if (entry.LineStatus is not "Picked" and not "Skipped")
                throw new InvalidOperationException(
                    $"Line {line.Id} status '{entry.LineStatus}' invalid — must be 'Picked' or 'Skipped'.");

            if (entry.LineStatus == "Picked")
            {
                if (entry.PickedQuantity is null)
                    throw new InvalidOperationException(
                        $"Line {line.Id} marked Picked but has no PickedQuantity.");
                if (entry.PickedQuantity < 0m || entry.PickedQuantity > line.ExpectedQuantity)
                    throw new InvalidOperationException(
                        $"Line {line.Id} PickedQuantity {entry.PickedQuantity} outside [0, {line.ExpectedQuantity}].");
                if (entry.PickedQuantity < line.ExpectedQuantity
                    && string.IsNullOrWhiteSpace(entry.ShortPickReason))
                    throw new InvalidOperationException(
                        $"Line {line.Id} short-picked ({entry.PickedQuantity} < {line.ExpectedQuantity}) — ShortPickReason required.");
            }
            else // Skipped
            {
                if (entry.PickedQuantity is not null)
                    throw new InvalidOperationException(
                        $"Line {line.Id} marked Skipped must have null PickedQuantity.");
                if (string.IsNullOrWhiteSpace(entry.ShortPickReason))
                    throw new InvalidOperationException(
                        $"Line {line.Id} skipped — ShortPickReason required.");
            }
        }

        return dict;
    }
}
