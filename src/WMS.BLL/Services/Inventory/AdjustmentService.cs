using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inventory;

// Phase 11A (ADR-013) — Stock Adjustment workflow service. Mirrors
// the Phase 10B cancellation pattern: TransactionScope wraps the
// Stock write + Movement Log INSERT (handled atomically inside
// StockRepository.UpsertOnHandAsync) + the status flip on
// inventory.Adjustments. Same MSDTC trade-off applies (multi-
// connection orchestration → ambient TX promotion). See
// feedback_transactionscope_dapper.md.
public sealed class AdjustmentService : IAdjustmentService
{
    private static readonly HashSet<string> ValidReasons = new(StringComparer.Ordinal)
    {
        "Damaged", "Expired", "Lost", "Found",
        "ReturnedToSupplier", "Sample", "Other",
    };

    private readonly IAdjustmentRepositoryFactory _adjRepoFactory;
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly ILogger<AdjustmentService> _logger;

    public AdjustmentService(
        IAdjustmentRepositoryFactory adjRepoFactory,
        IStockRepositoryFactory stockRepoFactory,
        ILogger<AdjustmentService> logger)
    {
        _adjRepoFactory = adjRepoFactory;
        _stockRepoFactory = stockRepoFactory;
        _logger = logger;
    }

    public async Task<Adjustment> CreateAsync(
        Guid tenantId,
        CreateAdjustmentRequest request,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        Validate(request);
        if (currentUserId == Guid.Empty)
            throw new ArgumentException(
                "RequestedBy is required (currentUserId must be set).",
                nameof(currentUserId));

        var repo = _adjRepoFactory.For(tenantId);

        // Assign next AdjustmentNumber for today's prefix.
        var datePrefix = $"ADJ-{DateTime.UtcNow:yyyyMMdd}-";
        var existing = await repo.CountForDatePrefixAsync(datePrefix, ct);
        var adjustmentNumber = $"{datePrefix}{(existing + 1):D4}";

        var entity = new Adjustment
        {
            Id = Guid.NewGuid(),
            AdjustmentNumber = adjustmentNumber,
            // StockId stays NULL on Pending — Apply resolves and stamps
            // it. Persisting the resolution at create-time would race
            // against intervening receives that change the row.
            StockId = null,
            LocationId = request.LocationId,
            ProductId = request.ProductId,
            LotId = request.LotId,
            PalletId = request.PalletId,
            OwnerId = request.OwnerId,
            UomId = request.UomId,
            WarehouseId = request.WarehouseId,
            QuantityDelta = request.QuantityDelta,
            Reason = request.Reason,
            Notes = request.Notes,
            Status = "Pending",
            // AllowCreateNew is a Pending-time hint — captured in Notes
            // so the approver sees it. Could be a separate column later
            // if needed.
        };

        await repo.CreateAsync(entity, currentUserId, ct);

        var saved = await repo.GetByIdAsync(entity.Id, ct)
            ?? throw new InvalidOperationException(
                $"Adjustment {entity.Id} not found immediately after create.");

        _logger.LogInformation(
            "Created adjustment {AdjustmentNumber} ({Id}) — delta {Delta} of product {ProductId} at location {LocationId} (reason: {Reason})",
            adjustmentNumber, entity.Id, request.QuantityDelta,
            request.ProductId, request.LocationId, request.Reason);

        return saved;
    }

    public async Task<bool> ApproveAsync(
        Guid tenantId,
        Guid adjustmentId,
        Guid approverUserId,
        CancellationToken ct = default)
    {
        if (approverUserId == Guid.Empty)
            throw new ArgumentException(
                "Approver user id is required.", nameof(approverUserId));

        var adjRepo = _adjRepoFactory.For(tenantId);
        var adjustment = await adjRepo.GetByIdAsync(adjustmentId, ct)
            ?? throw new InvalidOperationException(
                $"Adjustment {adjustmentId} not found.");

        // Idempotent on terminal states.
        if (adjustment.Status == "Applied")  return false;
        if (adjustment.Status == "Rejected")
            throw new InvalidOperationException(
                "Cannot approve a rejected adjustment.");
        if (adjustment.Status != "Pending")
            throw new InvalidOperationException(
                $"Cannot approve adjustment in state '{adjustment.Status}'.");

        // Separation of duties — requester cannot self-approve.
        if (adjustment.RequestedBy == approverUserId)
            throw new InvalidOperationException(
                "Adjustments cannot be self-approved (separation of duties).");

        // TransactionScope wraps Stock write + status flip. Multi-
        // connection → MSDTC promotion (per Phase 10B precedent +
        // memory entry feedback_transactionscope_dapper.md).
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions
            {
                IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,
            },
            TransactionScopeAsyncFlowOption.Enabled);

        var stockRepo = _stockRepoFactory.For(tenantId);

        var key = new StockKey(
            adjustment.LocationId,
            adjustment.ProductId,
            adjustment.LotId,
            adjustment.PalletId,
            adjustment.OwnerId,
            adjustment.UomId);

        var ctx = new StockMovementContext(
            MovementType: StockMovementType.Adjust,
            PerformedBy: approverUserId,
            ReferenceType: "Adjustment",
            ReferenceId: adjustment.Id,
            Notes: $"{adjustment.Reason}: {adjustment.AdjustmentNumber}");

        // UpsertOnHandAsync handles both signs (positive = found,
        // negative = damaged/loss); CK_Stock_OnHand_NonNegative throws
        // if the operator tried a -delta against insufficient stock.
        // WHEN NOT MATCHED branch creates a new Stock row when no
        // existing row matches the 6-tuple — fine for "found" scenarios
        // (positive delta). A negative delta on a non-existent row
        // would fail CK_Stock_OnHand_NonNegative naturally.
        var stock = await stockRepo.UpsertOnHandAsync(
            key, adjustment.QuantityDelta, ctx, ct);

        var changed = await adjRepo.SetAppliedAsync(
            adjustmentId, stock.Id, approverUserId, ct);

        if (!changed)
            throw new InvalidOperationException(
                $"Failed to apply adjustment {adjustmentId} — concurrent state change?");

        scope.Complete();

        _logger.LogInformation(
            "Approved + applied adjustment {AdjustmentNumber} ({Id}) — delta {Delta} → stock {StockId} (approver: {ApproverId})",
            adjustment.AdjustmentNumber, adjustmentId,
            adjustment.QuantityDelta, stock.Id, approverUserId);

        return true;
    }

    public async Task<bool> RejectAsync(
        Guid tenantId,
        Guid adjustmentId,
        string reason,
        Guid rejecterUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException(
                "Rejection reason is required.", nameof(reason));
        if (rejecterUserId == Guid.Empty)
            throw new ArgumentException(
                "Rejecter user id is required.", nameof(rejecterUserId));

        var repo = _adjRepoFactory.For(tenantId);
        var adjustment = await repo.GetByIdAsync(adjustmentId, ct)
            ?? throw new InvalidOperationException(
                $"Adjustment {adjustmentId} not found.");

        if (adjustment.Status == "Rejected") return false;
        if (adjustment.Status == "Applied")
            throw new InvalidOperationException(
                "Cannot reject an already-applied adjustment.");
        if (adjustment.Status != "Pending")
            throw new InvalidOperationException(
                $"Cannot reject adjustment in state '{adjustment.Status}'.");

        if (adjustment.RequestedBy == rejecterUserId)
            throw new InvalidOperationException(
                "Adjustments cannot be self-rejected (separation of duties).");

        var changed = await repo.SetRejectedAsync(
            adjustmentId, reason.Trim(), rejecterUserId, ct);

        if (changed)
        {
            _logger.LogInformation(
                "Rejected adjustment {AdjustmentNumber} ({Id}) — reason: {Reason} (rejecter: {RejecterId})",
                adjustment.AdjustmentNumber, adjustmentId, reason, rejecterUserId);
        }
        return changed;
    }

    public Task<Adjustment?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default) =>
        _adjRepoFactory.For(tenantId).GetByIdAsync(id, ct);

    public Task<Adjustment?> GetByNumberAsync(
        Guid tenantId, string adjustmentNumber, CancellationToken ct = default) =>
        _adjRepoFactory.For(tenantId).GetByNumberAsync(adjustmentNumber, ct);

    private static void Validate(CreateAdjustmentRequest req)
    {
        if (req.LocationId == Guid.Empty)
            throw new ArgumentException("LocationId is required.", nameof(req));
        if (req.ProductId == Guid.Empty)
            throw new ArgumentException("ProductId is required.", nameof(req));
        if (req.OwnerId == Guid.Empty)
            throw new ArgumentException("OwnerId is required.", nameof(req));
        if (req.UomId == Guid.Empty)
            throw new ArgumentException("UomId is required.", nameof(req));
        if (req.WarehouseId == Guid.Empty)
            throw new ArgumentException("WarehouseId is required.", nameof(req));
        if (req.QuantityDelta == 0m)
            throw new ArgumentException(
                "QuantityDelta must be non-zero.", nameof(req));
        if (string.IsNullOrWhiteSpace(req.Reason))
            throw new ArgumentException(
                "Reason is required.", nameof(req));
        if (!ValidReasons.Contains(req.Reason))
            throw new ArgumentException(
                $"Reason '{req.Reason}' is not in the allowed list.", nameof(req));
    }
}
