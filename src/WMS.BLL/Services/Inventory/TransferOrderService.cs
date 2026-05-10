using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inventory;

// Phase 13 (ADR-012) — TransferOrder workflow service. Mirrors
// Phase 11A/12 TransactionScope pattern for actions that touch Stock
// (Dispatch, Receive). MSDTC trade-off applies (per memory
// feedback_transactionscope_dapper.md).
public sealed class TransferOrderService : ITransferOrderService
{
    private static readonly HashSet<string> ValidReasons = new(StringComparer.Ordinal)
    {
        "Rebalance", "Replenish", "CustomerOrder", "Other",
    };

    private readonly ITransferOrderRepositoryFactory _repoFactory;
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly ILogger<TransferOrderService> _logger;

    public TransferOrderService(
        ITransferOrderRepositoryFactory repoFactory,
        IStockRepositoryFactory stockRepoFactory,
        ILogger<TransferOrderService> logger)
    {
        _repoFactory = repoFactory;
        _stockRepoFactory = stockRepoFactory;
        _logger = logger;
    }

    public async Task<TransferOrderDetail> CreateAsync(
        Guid tenantId,
        CreateTransferOrderRequest request,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        Validate(request);
        if (currentUserId == Guid.Empty)
            throw new ArgumentException("RequestedBy is required.", nameof(currentUserId));

        var repo = _repoFactory.For(tenantId);

        var datePrefix = $"TRN-{DateTime.UtcNow:yyyyMMdd}-";
        var existing = await repo.CountForDatePrefixAsync(datePrefix, ct);
        var transferNumber = $"{datePrefix}{(existing + 1):D4}";

        var headerId = Guid.NewGuid();
        var header = new TransferOrder
        {
            Id = headerId,
            TransferNumber = transferNumber,
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            Status = "Draft",
            Reason = request.Reason,
            Notes = request.Notes,
            RequestedBy = currentUserId,
        };

        var lines = request.Lines
            .Select(l => new TransferOrderLine
            {
                Id = Guid.NewGuid(),
                TransferId = headerId,
                LineNumber = l.LineNumber,
                ProductId = l.ProductId,
                OwnerId = l.OwnerId,
                UomId = l.UomId,
                LotId = l.LotId,
                PalletId = l.PalletId,
                FromLocationId = l.FromLocationId,
                ToLocationId = l.ToLocationId,
                QtyRequested = l.QtyRequested,
                Status = "Pending",
                Notes = l.Notes,
            })
            .ToList();

        await repo.CreateAsync(header, lines, currentUserId, ct);

        var detail = await repo.GetByIdAsync(headerId, ct)
            ?? throw new InvalidOperationException(
                $"TransferOrder {headerId} not found immediately after create.");

        _logger.LogInformation(
            "Created transfer {TransferNumber} ({Id}) — {LineCount} lines from {FromWh} to {ToWh}",
            transferNumber, headerId, lines.Count,
            request.FromWarehouseId, request.ToWarehouseId);

        return detail;
    }

    public async Task<bool> SubmitAsync(
        Guid tenantId, Guid transferId, Guid currentUserId, CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(transferId, ct)
            ?? throw new InvalidOperationException($"TransferOrder {transferId} not found.");

        if (detail.Header.Status == "Submitted") return false;
        if (detail.Header.Status != "Draft")
            throw new InvalidOperationException(
                $"Cannot submit in state '{detail.Header.Status}'.");

        return await repo.SetSubmittedAsync(transferId, currentUserId, ct);
    }

    public async Task<bool> ApproveAsync(
        Guid tenantId, Guid transferId, Guid approverUserId, CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(transferId, ct)
            ?? throw new InvalidOperationException($"TransferOrder {transferId} not found.");

        if (detail.Header.Status == "Approved") return false;
        if (detail.Header.Status != "Submitted")
            throw new InvalidOperationException(
                $"Cannot approve in state '{detail.Header.Status}' — submit first.");

        // Separation of duties — approver != requester.
        if (detail.Header.RequestedBy == approverUserId)
            throw new InvalidOperationException(
                "Transfer cannot be self-approved (separation of duties).");

        var changed = await repo.SetApprovedAsync(transferId, approverUserId, ct);
        if (changed)
            _logger.LogInformation(
                "Approved transfer {TransferNumber} ({Id}) — approver {ApproverId}",
                detail.Header.TransferNumber, transferId, approverUserId);
        return changed;
    }

    public async Task<bool> DispatchAsync(
        Guid tenantId,
        Guid transferId,
        IReadOnlyList<DispatchLineRequest> lines,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new ArgumentException("At least one line dispatch required.", nameof(lines));

        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(transferId, ct)
            ?? throw new InvalidOperationException($"TransferOrder {transferId} not found.");

        if (detail.Header.Status == "InTransit") return false;
        if (detail.Header.Status != "Approved")
            throw new InvalidOperationException(
                $"Cannot dispatch in state '{detail.Header.Status}' — approve first.");

        // Index source lines for validation.
        var byId = detail.Lines.ToDictionary(l => l.Id);
        foreach (var d in lines)
        {
            if (!byId.TryGetValue(d.LineId, out var sourceLine))
                throw new ArgumentException(
                    $"Line {d.LineId} doesn't belong to transfer {transferId}.",
                    nameof(lines));
            if (sourceLine.Status != "Pending")
                throw new InvalidOperationException(
                    $"Line {sourceLine.LineNumber} is already in state '{sourceLine.Status}'.");
            if (d.QtyDispatched <= 0m)
                throw new ArgumentException(
                    $"Line {sourceLine.LineNumber}: QtyDispatched must be positive.",
                    nameof(lines));
            if (d.QtyDispatched > sourceLine.QtyRequested)
                throw new ArgumentException(
                    $"Line {sourceLine.LineNumber}: QtyDispatched ({d.QtyDispatched}) cannot exceed QtyRequested ({sourceLine.QtyRequested}).",
                    nameof(lines));
        }

        // TransactionScope — Stock decrements + line updates + header
        // transition all-or-nothing.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        var stockRepo = _stockRepoFactory.For(tenantId);

        foreach (var d in lines)
        {
            var sourceLine = byId[d.LineId];
            var key = new StockKey(
                sourceLine.FromLocationId, sourceLine.ProductId,
                sourceLine.LotId, sourceLine.PalletId,
                sourceLine.OwnerId, sourceLine.UomId);

            var ctx = new StockMovementContext(
                MovementType: StockMovementType.Transfer,
                PerformedBy: currentUserId,
                ReferenceType: "TransferOrderLine",
                ReferenceId: sourceLine.Id,
                Notes: $"Dispatched (transfer {detail.Header.TransferNumber})");

            // Negative delta — source decrements. CK_Stock_OnHand
            // _NonNegative throws if insufficient — TX rolls back.
            await stockRepo.UpsertOnHandAsync(key, -d.QtyDispatched, ctx, ct);

            await repo.UpdateLineDispatchedAsync(
                d.LineId, d.QtyDispatched, currentUserId, ct);
        }

        var statusChanged = await repo.SetInTransitAsync(transferId, currentUserId, ct);
        if (!statusChanged)
            throw new InvalidOperationException(
                $"Failed to dispatch transfer {transferId} — concurrent state change?");

        scope.Complete();

        _logger.LogInformation(
            "Dispatched transfer {TransferNumber} ({Id}) — {LineCount} lines (dispatcher {UserId})",
            detail.Header.TransferNumber, transferId, lines.Count, currentUserId);

        return true;
    }

    public async Task<bool> ReceiveAsync(
        Guid tenantId,
        Guid transferId,
        IReadOnlyList<ReceiveLineRequest> lines,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new ArgumentException("At least one line receive required.", nameof(lines));

        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(transferId, ct)
            ?? throw new InvalidOperationException($"TransferOrder {transferId} not found.");

        if (detail.Header.Status == "Received") return false;
        if (detail.Header.Status != "InTransit")
            throw new InvalidOperationException(
                $"Cannot receive in state '{detail.Header.Status}'.");

        var byId = detail.Lines.ToDictionary(l => l.Id);
        foreach (var r in lines)
        {
            if (!byId.TryGetValue(r.LineId, out var sourceLine))
                throw new ArgumentException(
                    $"Line {r.LineId} doesn't belong to transfer {transferId}.",
                    nameof(lines));
            if (sourceLine.Status != "Dispatched")
                throw new InvalidOperationException(
                    $"Line {sourceLine.LineNumber} is in state '{sourceLine.Status}' — only Dispatched can be received.");
            if (sourceLine.QtyDispatched is null)
                throw new InvalidOperationException(
                    $"Line {sourceLine.LineNumber} has no QtyDispatched — data inconsistency.");
            if (r.QtyReceived < 0m)
                throw new ArgumentException(
                    $"Line {sourceLine.LineNumber}: QtyReceived cannot be negative.",
                    nameof(lines));
            if (r.QtyReceived > sourceLine.QtyDispatched.Value)
                throw new ArgumentException(
                    $"Line {sourceLine.LineNumber}: QtyReceived ({r.QtyReceived}) cannot exceed QtyDispatched ({sourceLine.QtyDispatched.Value}).",
                    nameof(lines));
        }

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        var stockRepo = _stockRepoFactory.For(tenantId);

        foreach (var r in lines)
        {
            var sourceLine = byId[r.LineId];

            // Variance status when QtyReceived < QtyDispatched.
            var targetStatus = r.QtyReceived < sourceLine.QtyDispatched!.Value
                ? "Variance"
                : "Received";

            // Only write Stock when we received something. Zero-receive
            // (whole shipment lost on this line) skips the Stock write
            // but still records the variance.
            if (r.QtyReceived > 0m)
            {
                var key = new StockKey(
                    sourceLine.ToLocationId, sourceLine.ProductId,
                    sourceLine.LotId, sourceLine.PalletId,
                    sourceLine.OwnerId, sourceLine.UomId);

                var ctx = new StockMovementContext(
                    MovementType: StockMovementType.Transfer,
                    PerformedBy: currentUserId,
                    ReferenceType: "TransferOrderLine",
                    ReferenceId: sourceLine.Id,
                    Notes: $"Received (transfer {detail.Header.TransferNumber})");

                await stockRepo.UpsertOnHandAsync(key, r.QtyReceived, ctx, ct);
            }

            await repo.UpdateLineReceivedAsync(
                r.LineId, r.QtyReceived, targetStatus, currentUserId, ct);
        }

        var statusChanged = await repo.SetReceivedAsync(transferId, currentUserId, ct);
        if (!statusChanged)
            throw new InvalidOperationException(
                $"Failed to receive transfer {transferId} — concurrent state change?");

        scope.Complete();

        _logger.LogInformation(
            "Received transfer {TransferNumber} ({Id}) — {LineCount} lines (receiver {UserId})",
            detail.Header.TransferNumber, transferId, lines.Count, currentUserId);

        return true;
    }

    public async Task<bool> CancelAsync(
        Guid tenantId, Guid transferId, string reason, Guid currentUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancel reason is required.", nameof(reason));

        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(transferId, ct)
            ?? throw new InvalidOperationException($"TransferOrder {transferId} not found.");

        if (detail.Header.Status == "Cancelled") return false;
        if (detail.Header.Status is "InTransit" or "Received" or "Lost")
            throw new InvalidOperationException(
                $"Cannot cancel in state '{detail.Header.Status}'.");

        // Try each pre-InTransit source state. Whichever applies wins.
        var changed =
               await repo.SetCancelledAsync(transferId, "Draft",     reason.Trim(), currentUserId, ct)
            || await repo.SetCancelledAsync(transferId, "Submitted", reason.Trim(), currentUserId, ct)
            || await repo.SetCancelledAsync(transferId, "Approved",  reason.Trim(), currentUserId, ct);

        if (changed)
            _logger.LogInformation(
                "Cancelled transfer {TransferNumber} ({Id}) — reason: {Reason}",
                detail.Header.TransferNumber, transferId, reason);
        return changed;
    }

    public async Task<bool> MarkLostAsync(
        Guid tenantId, Guid transferId, string reason, Guid currentUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Loss reason is required.", nameof(reason));

        var repo = _repoFactory.For(tenantId);
        var detail = await repo.GetByIdAsync(transferId, ct)
            ?? throw new InvalidOperationException($"TransferOrder {transferId} not found.");

        if (detail.Header.Status == "Lost") return false;
        if (detail.Header.Status != "InTransit")
            throw new InvalidOperationException(
                $"Cannot mark as Lost in state '{detail.Header.Status}' — only InTransit can be Lost.");

        var changed = await repo.SetLostAsync(transferId, reason.Trim(), currentUserId, ct);
        if (changed)
            _logger.LogInformation(
                "Marked transfer {TransferNumber} ({Id}) as Lost — reason: {Reason}",
                detail.Header.TransferNumber, transferId, reason);
        return changed;
    }

    public Task<TransferOrderDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByIdAsync(id, ct);

    public Task<TransferOrderDetail?> GetByNumberAsync(
        Guid tenantId, string transferNumber, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByNumberAsync(transferNumber, ct);

    private static void Validate(CreateTransferOrderRequest req)
    {
        if (req.FromWarehouseId == Guid.Empty)
            throw new ArgumentException("FromWarehouseId is required.", nameof(req));
        if (req.ToWarehouseId == Guid.Empty)
            throw new ArgumentException("ToWarehouseId is required.", nameof(req));
        if (req.FromWarehouseId == req.ToWarehouseId)
            throw new ArgumentException("From and To warehouse must differ.", nameof(req));
        if (string.IsNullOrWhiteSpace(req.Reason))
            throw new ArgumentException("Reason is required.", nameof(req));
        if (!ValidReasons.Contains(req.Reason))
            throw new ArgumentException(
                $"Reason '{req.Reason}' is not in the allowed list.", nameof(req));
        if (req.Lines is null || req.Lines.Count == 0)
            throw new ArgumentException("At least one line is required.", nameof(req));

        var seenNumbers = new HashSet<int>();
        foreach (var line in req.Lines)
        {
            if (line.LineNumber <= 0)
                throw new ArgumentException(
                    $"LineNumber must be positive (got {line.LineNumber}).", nameof(req));
            if (!seenNumbers.Add(line.LineNumber))
                throw new ArgumentException(
                    $"Duplicate LineNumber {line.LineNumber}.", nameof(req));
            if (line.ProductId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: ProductId is required.", nameof(req));
            if (line.OwnerId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: OwnerId is required.", nameof(req));
            if (line.UomId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: UomId is required.", nameof(req));
            if (line.FromLocationId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: FromLocationId is required.", nameof(req));
            if (line.ToLocationId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: ToLocationId is required.", nameof(req));
            if (line.FromLocationId == line.ToLocationId)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: From and To location must differ.", nameof(req));
            if (line.QtyRequested <= 0m)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: QtyRequested must be positive.", nameof(req));
        }
    }
}
