using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Counts;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Counts;

namespace WMS.BLL.Services.Counts;

// Phase 12 — cycle count workflow service. Mirrors the Phase 10B/11A
// TransactionScope pattern for ApproveAndApply: Stock writes + repo
// status flip in one ambient TX. Same MSDTC trade-off applies (per
// memory feedback_transactionscope_dapper.md).
public sealed class CycleCountService : ICycleCountService
{
    private readonly ICycleCountRepositoryFactory _repoFactory;
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly ILogger<CycleCountService> _logger;

    public CycleCountService(
        ICycleCountRepositoryFactory repoFactory,
        IStockRepositoryFactory stockRepoFactory,
        ILogger<CycleCountService> logger)
    {
        _repoFactory = repoFactory;
        _stockRepoFactory = stockRepoFactory;
        _logger = logger;
    }

    public async Task<CycleCountDetail> CreateAsync(
        Guid tenantId,
        CreateCycleCountRequest request,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (request.WarehouseId == Guid.Empty)
            throw new ArgumentException("WarehouseId is required.", nameof(request));
        if (currentUserId == Guid.Empty)
            throw new ArgumentException("StartedBy is required.", nameof(currentUserId));

        // Snapshot positive-OnHand stock at scope.
        var stockRepo = _stockRepoFactory.For(tenantId);
        var snapshot = await stockRepo.GetPositiveOnHandByWarehouseAsync(
            request.WarehouseId, request.LocationFilter, ct);

        if (snapshot.Count == 0)
            throw new InvalidOperationException(
                "No positive-OnHand stock at the requested scope — nothing to count.");

        var repo = _repoFactory.For(tenantId);

        // Assign CountNumber.
        var datePrefix = $"CYC-{DateTime.UtcNow:yyyyMMdd}-";
        var existing = await repo.CountForDatePrefixAsync(datePrefix, ct);
        var countNumber = $"{datePrefix}{(existing + 1):D4}";

        var headerId = Guid.NewGuid();
        var header = new CycleCount
        {
            Id = headerId,
            CountNumber = countNumber,
            WarehouseId = request.WarehouseId,
            LocationFilter = request.LocationFilter,
            Status = "Counting",
            Notes = request.Notes,
            StartedBy = currentUserId,
        };

        var lines = snapshot
            .Select((s, i) => new CycleCountLine
            {
                Id = Guid.NewGuid(),
                CycleCountId = headerId,
                LineNumber = i + 1,
                StockId = s.Id,
                LocationId = s.LocationId,
                ProductId = s.ProductId,
                LotId = s.LotId,
                PalletId = s.PalletId,
                OwnerId = s.OwnerId,
                UomId = s.UomId,
                ExpectedQuantity = s.QuantityOnHand,
                CountedQuantity = null,
                LineStatus = "Pending",
            })
            .ToList();

        await repo.CreateAsync(header, lines, currentUserId, ct);

        var detail = await repo.GetByIdAsync(headerId, ct)
            ?? throw new InvalidOperationException(
                $"CycleCount {headerId} not found immediately after create.");

        _logger.LogInformation(
            "Created cycle count {CountNumber} ({Id}) — {LineCount} lines snapshot at warehouse {WarehouseId} (location filter: {LocationFilter})",
            countNumber, headerId, lines.Count, request.WarehouseId, request.LocationFilter);

        return detail;
    }

    public async Task SaveCountedQuantitiesAsync(
        Guid tenantId,
        Guid cycleCountId,
        IReadOnlyList<CountLineUpdate> updates,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(cycleCountId, ct)
            ?? throw new InvalidOperationException(
                $"CycleCount {cycleCountId} not found.");

        if (detail.Header.Status != "Counting")
            throw new InvalidOperationException(
                $"Cannot save counts in state '{detail.Header.Status}' — only Counting allowed.");

        // Validate per-update LineStatus values + count quantity rules.
        foreach (var u in updates)
        {
            if (u.LineStatus is not ("Pending" or "Counted" or "Skipped"))
                throw new ArgumentException(
                    $"Invalid LineStatus '{u.LineStatus}' for line {u.LineId}.");

            if (u.LineStatus == "Counted" && u.CountedQuantity is null)
                throw new ArgumentException(
                    $"Line {u.LineId}: LineStatus=Counted requires CountedQuantity.");

            if (u.LineStatus == "Pending" && u.CountedQuantity is not null)
                throw new ArgumentException(
                    $"Line {u.LineId}: LineStatus=Pending forbids CountedQuantity (clear it first).");

            if (u.CountedQuantity is { } qty && qty < 0)
                throw new ArgumentException(
                    $"Line {u.LineId}: CountedQuantity must be non-negative.");
        }

        await repo.SaveCountedQuantitiesAsync(
            cycleCountId,
            updates.Select(u => (u.LineId, u.CountedQuantity, u.LineStatus, u.Notes)).ToList(),
            currentUserId,
            ct);
    }

    public async Task<bool> SubmitForReviewAsync(
        Guid tenantId, Guid cycleCountId, Guid currentUserId, CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(cycleCountId, ct)
            ?? throw new InvalidOperationException(
                $"CycleCount {cycleCountId} not found.");

        if (detail.Header.Status == "Review")  return false;  // idempotent
        if (detail.Header.Status != "Counting")
            throw new InvalidOperationException(
                $"Cannot submit for review in state '{detail.Header.Status}'.");

        var changed = await repo.SetSubmittedForReviewAsync(cycleCountId, currentUserId, ct);
        if (changed)
        {
            _logger.LogInformation(
                "Cycle count {CountNumber} submitted for review by {UserId}",
                detail.Header.CountNumber, currentUserId);
        }
        return changed;
    }

    public async Task<bool> ApproveAndApplyAsync(
        Guid tenantId, Guid cycleCountId, Guid approverUserId, CancellationToken ct = default)
    {
        if (approverUserId == Guid.Empty)
            throw new ArgumentException("Approver user id is required.", nameof(approverUserId));

        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(cycleCountId, ct)
            ?? throw new InvalidOperationException(
                $"CycleCount {cycleCountId} not found.");

        if (detail.Header.Status == "Applied") return false;  // idempotent
        if (detail.Header.Status == "Cancelled")
            throw new InvalidOperationException("Cannot approve a cancelled cycle count.");
        if (detail.Header.Status != "Review")
            throw new InvalidOperationException(
                $"Cannot approve in state '{detail.Header.Status}' — submit for review first.");

        // Separation of duties — counter cannot self-approve.
        if (detail.Header.CountedBy == approverUserId)
            throw new InvalidOperationException(
                "Cycle count cannot be self-approved (separation of duties).");

        // TransactionScope wraps Stock writes + status flip. Multi-
        // connection → MSDTC promotion (per Phase 10B/11A precedent).
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions
            {
                IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,
            },
            TransactionScopeAsyncFlowOption.Enabled);

        var stockRepo = _stockRepoFactory.For(tenantId);

        // Per-line apply: write Stock + Movement Log only when the
        // line was Counted with non-zero variance. Skipped/Pending
        // lines pass through silently.
        var appliedLines = 0;
        foreach (var line in detail.Lines)
        {
            if (line.LineStatus != "Counted") continue;
            if (line.CountedQuantity is not { } counted) continue;

            var variance = counted - line.ExpectedQuantity;
            if (variance == 0m) continue;  // verified-as-correct

            var key = new StockKey(
                line.LocationId, line.ProductId, line.LotId, line.PalletId,
                line.OwnerId, line.UomId);

            var ctx = new StockMovementContext(
                MovementType: StockMovementType.Cycle,
                PerformedBy: approverUserId,
                ReferenceType: "CycleCountLine",
                ReferenceId: line.Id,
                Notes: $"Cycle count {detail.Header.CountNumber} reconciliation");

            // UpsertOnHandAsync handles both signs. CK_Stock_OnHand
            // _NonNegative throws if a negative variance would
            // underflow (operator picked from this stock since
            // counting started — TX rolls back).
            await stockRepo.UpsertOnHandAsync(key, variance, ctx, ct);
            appliedLines++;
        }

        var statusChanged = await repo.SetAppliedAsync(cycleCountId, approverUserId, ct);
        if (!statusChanged)
            throw new InvalidOperationException(
                $"Failed to apply cycle count {cycleCountId} — concurrent state change?");

        scope.Complete();

        _logger.LogInformation(
            "Applied cycle count {CountNumber} ({Id}) — {AppliedLines} of {TotalLines} lines had non-zero variance (approver: {ApproverId})",
            detail.Header.CountNumber, cycleCountId, appliedLines, detail.Lines.Count, approverUserId);

        return true;
    }

    public async Task<bool> CancelAsync(
        Guid tenantId, Guid cycleCountId, string reason, Guid currentUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancel reason is required.", nameof(reason));

        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(cycleCountId, ct)
            ?? throw new InvalidOperationException(
                $"CycleCount {cycleCountId} not found.");

        if (detail.Header.Status == "Cancelled") return false;  // idempotent
        if (detail.Header.Status == "Applied")
            throw new InvalidOperationException(
                "Cannot cancel an applied cycle count.");

        // Allow from Counting OR Review.
        var changed =
               await repo.SetCancelledAsync(cycleCountId, "Counting", reason.Trim(), currentUserId, ct)
            || await repo.SetCancelledAsync(cycleCountId, "Review",   reason.Trim(), currentUserId, ct);

        if (changed)
        {
            _logger.LogInformation(
                "Cancelled cycle count {CountNumber} ({Id}) — reason: {Reason}",
                detail.Header.CountNumber, cycleCountId, reason);
        }
        return changed;
    }

    public Task<CycleCountDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByIdAsync(id, ct);

    public Task<CycleCountDetail?> GetByNumberAsync(
        Guid tenantId, string countNumber, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByNumberAsync(countNumber, ct);
}
