using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inbound;

namespace WMS.BLL.Services.Inbound;

// Orchestrates the post-receipt flow.
//
// Phase 10B (TD-022): the full orchestration runs inside an ambient
// `TransactionScope` so any failure in steps 2-4 (per-line stock
// upsert, lot/pallet link-back, PO line bump, PO status transitions)
// rolls back step 1's header+lines write too. The repos detect the
// ambient scope and skip their own internal `BeginTransaction()`
// calls (which would otherwise throw "SqlConnection does not support
// parallel transactions").
//
// Trade-off: each repo gets a fresh `SqlConnection` from the factory,
// so this orchestration involves multiple connections within one
// scope. SqlClient promotes the LTM transaction to MSDTC on the
// second connection enlist. Acceptable because:
//   * Windows-only deployment (per architecture) — MSDTC is the
//     standard distributed-coordinator service and is enabled by
//     default on most Windows installations.
//   * Production single-instance volume doesn't stress DTC.
//   * The alternative (threading one shared connection through every
//     repo + sub-service) is a much larger refactor for marginal
//     gain at current volumes.
// If MSDTC ever becomes operational burden, swap this for the
// connection-threading pattern; the repo-side ambient detection
// stays useful either way.
public sealed class ReceivingHeaderService : IReceivingHeaderService
{
    private readonly IReceivingHeaderRepositoryFactory _receivingRepoFactory;
    private readonly IPurchaseOrderRepositoryFactory _poRepoFactory;
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly IReceivingService _receivingService;
    private readonly IPurchaseOrderService _purchaseOrderService;
    private readonly ILogger<ReceivingHeaderService> _logger;

    public ReceivingHeaderService(
        IReceivingHeaderRepositoryFactory receivingRepoFactory,
        IPurchaseOrderRepositoryFactory poRepoFactory,
        IStockRepositoryFactory stockRepoFactory,
        IReceivingService receivingService,
        IPurchaseOrderService purchaseOrderService,
        ILogger<ReceivingHeaderService> logger)
    {
        _receivingRepoFactory = receivingRepoFactory;
        _poRepoFactory = poRepoFactory;
        _stockRepoFactory = stockRepoFactory;
        _receivingService = receivingService;
        _purchaseOrderService = purchaseOrderService;
        _logger = logger;
    }

    public async Task<ReceivingDetail> PostReceivingAsync(
        Guid tenantId,
        PostReceivingRequest request,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        Validate(request);

        var headerId = Guid.NewGuid();
        var receivedAt = request.ReceivedAt ?? DateTime.UtcNow;

        var header = new ReceivingHeader
        {
            Id = headerId,
            ReceivingNumber = request.ReceivingNumber,
            PurchaseOrderId = request.PurchaseOrderId,
            WarehouseId = request.WarehouseId,
            ReceivedAt = receivedAt,
            Status = request.IsDraft ? "Draft" : "Posted",
            Notes = request.Notes,
        };

        // Initial line set has LotId / PalletId = null; orchestration
        // populates them after the β stock upserts resolve the rows.
        var lines = request.Lines
            .Select(l => new ReceivingLine
            {
                Id = Guid.NewGuid(),
                ReceivingHeaderId = headerId,
                LineNumber = l.LineNumber,
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                ProductId = l.ProductId,
                UomId = l.UomId,
                OwnerId = l.OwnerId,
                LocationId = l.LocationId,
                ReceivedQuantity = l.ReceivedQuantity,
                LotNumber = l.Lot?.LotNumber,
                PalletNumber = l.Pallet?.PalletNumber,
            })
            .ToList();

        // TD-022 — TransactionScope wraps steps 1→6 below so any
        // mid-flight failure rolls back ALL writes (header + lines +
        // stock + Movement Log + PO line bumps + PO status flips).
        // ReadCommitted matches the repos' defaults; AsyncFlowOption
        // .Enabled is required so the ambient TX flows across the
        // `await` continuations Dapper produces.
        //
        // Draft path stays inside the scope but only runs Step 1, so
        // a TX-time failure on the header/lines insert still rolls
        // back cleanly.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions
            {
                IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,
            },
            TransactionScopeAsyncFlowOption.Enabled);

        // Step 1 — persist header + lines atomically.
        var receivingRepo = _receivingRepoFactory.For(tenantId);
        await receivingRepo.CreateAsync(header, lines, currentUserId, ct);

        // Phase 9B — Draft path skips stock orchestration. Header +
        // lines exist (audit trail of intent); stock untouched; no
        // Movement Log; no PO bumps; no PO status transitions.
        // The Phase 9C+ Post-Existing-Draft flow will complete the
        // orchestration when the operator confirms Post.
        if (request.IsDraft)
        {
            var draftDetail = await receivingRepo.GetByIdAsync(headerId, ct)
                ?? throw new InvalidOperationException(
                    $"Draft ReceivingHeader {headerId} not found immediately after create.");
            scope.Complete();
            _logger.LogInformation(
                "Saved DRAFT receiving {ReceivingNumber} ({HeaderId}) with {LineCount} line(s) " +
                "against PO {PurchaseOrderId} at warehouse {WarehouseId}",
                request.ReceivingNumber, headerId, lines.Count,
                request.PurchaseOrderId, request.WarehouseId);
            return draftDetail;
        }

        var poRepo = _poRepoFactory.For(tenantId);

        // Steps 2 → 4 — per line: stock upsert, link lot/pallet back,
        // bump PO line if linked.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var req = request.Lines[i];

            // line.Id is already on the books from Step 1's
            // CreateAsync — pass it through as the ReferenceId so the
            // matching StockMovements row traces back to this receiving
            // line. ADR-014: ReferenceType='ReceivingLine'.
            var stock = await _receivingService.ReceiveStockAsync(
                tenantId,
                new ReceiveStockRequest(
                    LocationId: req.LocationId,
                    ProductId: req.ProductId,
                    OwnerId: req.OwnerId,
                    UomId: req.UomId,
                    Quantity: req.ReceivedQuantity,
                    Lot: req.Lot,
                    Pallet: req.Pallet,
                    ReceivingLineId: line.Id),
                currentUserId,
                ct);

            // Stamp the resolved Lot / Pallet ids on the receiving line
            // even when one or both are null — keeps the audit trail
            // consistent with what β actually did.
            await receivingRepo.UpdateLineInventoryRefsAsync(
                line.Id, stock.LotId, stock.PalletId, currentUserId, ct);

            if (req.PurchaseOrderLineId is { } poLineId)
            {
                await poRepo.IncrementLineReceivedQuantityAsync(
                    poLineId, req.ReceivedQuantity, currentUserId, ct);
            }
        }

        // Phase 9B — PO status auto-transitions. Idempotent; safe
        // to call regardless of whether this is the first or Nth
        // receipt against the PO. Order matters: try MarkReceiving
        // first (Open → Receiving), then MarkClosed (any → Closed
        // when AllLinesFullyReceived). A single-receipt PO that
        // closes out completely will see MarkReceiving change the
        // state then MarkClosed flip it again — both succeed.
        if (request.PurchaseOrderId is { } poId)
        {
            await _purchaseOrderService.MarkReceivingAsync(
                tenantId, poId, currentUserId, ct);
            await _purchaseOrderService.MarkClosedAsync(
                tenantId, poId, currentUserId, ct);
        }

        // Re-fetch — gives the caller the post-update Detail, including
        // the Lot/Pallet refs the orchestration just stamped.
        var detail = await receivingRepo.GetByIdAsync(headerId, ct)
            ?? throw new InvalidOperationException(
                $"ReceivingHeader {headerId} not found immediately after post.");

        // TD-022 — commit the ambient TransactionScope. Failure to
        // reach Complete() rolls back everything in steps 1→6 plus
        // any nested writes (Lot/Pallet GetOrCreate, Movement Log).
        scope.Complete();

        _logger.LogInformation(
            "Posted receiving {ReceivingNumber} ({HeaderId}) with {LineCount} line(s) " +
            "against PO {PurchaseOrderId} at warehouse {WarehouseId}",
            request.ReceivingNumber, headerId, lines.Count,
            request.PurchaseOrderId, request.WarehouseId);

        return detail;
    }

    public Task<ReceivingDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default) =>
        _receivingRepoFactory.For(tenantId).GetByIdAsync(id, ct);

    public Task<ReceivingDetail?> GetByNumberAsync(
        Guid tenantId, string receivingNumber, CancellationToken ct = default) =>
        _receivingRepoFactory.For(tenantId).GetByNumberAsync(receivingNumber, ct);

    public async Task<bool> CancelReceivingAsync(
        Guid tenantId,
        Guid receivingHeaderId,
        string reason,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason is required.", nameof(reason));

        var receivingRepo = _receivingRepoFactory.For(tenantId);
        var detail = await receivingRepo.GetByIdAsync(receivingHeaderId, ct)
            ?? throw new InvalidOperationException(
                $"ReceivingHeader {receivingHeaderId} not found.");

        // State guard. Idempotent on already-Cancelled (returns false
        // up the stack — controller surfaces a "already cancelled"
        // notice). Drafts route through a future DiscardDraft flow
        // that simply deletes header + lines (no movements to reverse).
        if (detail.Header.Status == "Cancelled") return false;
        if (detail.Header.Status == "Draft")
            throw new InvalidOperationException(
                "Drafts cannot be cancelled — discard the draft instead.");
        if (detail.Header.Status != "Posted")
            throw new InvalidOperationException(
                $"Cannot cancel receipt in state '{detail.Header.Status}'.");

        // TD-022 — same TransactionScope pattern as PostReceivingAsync.
        // Multi-connection orchestration → MSDTC promotion. Repos
        // detect the ambient TX and skip their own BeginTransaction()
        // calls.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions
            {
                IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,
            },
            TransactionScopeAsyncFlowOption.Enabled);

        var stockRepo = _stockRepoFactory.For(tenantId);
        var poRepo    = _poRepoFactory.For(tenantId);

        // Step 1 — per line: subtract from Stock + write paired
        // negative StockMovement (MovementType=Adjust, RT=
        // 'ReceivingLineCancellation', RI=line.Id).
        // CK_Stock_OnHand_NonNegative throws if stock has been
        // consumed since the receive — TX rollback, operator told
        // to adjust manually first.
        foreach (var line in detail.Lines)
        {
            var key = new StockKey(
                line.LocationId,
                line.ProductId,
                line.LotId,
                line.PalletId,
                line.OwnerId,
                line.UomId);

            var ctx = new StockMovementContext(
                MovementType: StockMovementType.Adjust,
                PerformedBy:  currentUserId,
                ReferenceType: "ReceivingLineCancellation",
                ReferenceId:   line.Id,
                Notes: $"Cancelled receipt {detail.Header.ReceivingNumber}: {reason}");

            await stockRepo.UpsertOnHandAsync(key, -line.ReceivedQuantity, ctx, ct);

            // Step 2 — decrement linked PO line's ReceivedQuantity.
            if (line.PurchaseOrderLineId is { } poLineId)
            {
                await poRepo.IncrementLineReceivedQuantityAsync(
                    poLineId, -line.ReceivedQuantity, currentUserId, ct);
            }
        }

        // Step 3 — flip header status + audit trio. Idempotent at
        // SQL level (WHERE Status='Posted') — but we already gated
        // above so this should change exactly one row.
        var statusChanged = await receivingRepo.SetCancellationAsync(
            receivingHeaderId, reason, currentUserId, ct);

        if (!statusChanged)
            throw new InvalidOperationException(
                $"Failed to cancel ReceivingHeader {receivingHeaderId} — concurrent state change?");

        // Step 4 — revert PO line statuses (per-line server-side
        // CASE based on current Received vs Expected). Cancelled
        // PO lines stay Cancelled.
        foreach (var line in detail.Lines)
        {
            if (line.PurchaseOrderLineId is { } poLineId)
            {
                await poRepo.RevertLineStatusAsync(poLineId, currentUserId, ct);
            }
        }

        // Step 5 — revert PO header (Closed → Receiving / Open).
        if (detail.Header.PurchaseOrderId is { } poId)
        {
            await _purchaseOrderService.RevertStatusAfterCancelAsync(
                tenantId, poId, currentUserId, ct);
        }

        scope.Complete();

        _logger.LogInformation(
            "Cancelled receipt {ReceivingNumber} ({HeaderId}) — reason: {Reason}",
            detail.Header.ReceivingNumber, receivingHeaderId, reason);

        return true;
    }

    private static void Validate(PostReceivingRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ReceivingNumber))
            throw new ArgumentException("ReceivingNumber is required.", nameof(req));

        if (req.WarehouseId == Guid.Empty)
            throw new ArgumentException("WarehouseId is required.", nameof(req));

        if (req.Lines is null || req.Lines.Count == 0)
            throw new ArgumentException("At least one line is required.", nameof(req));

        var seenLineNumbers = new HashSet<int>();
        foreach (var line in req.Lines)
        {
            if (line.LineNumber <= 0)
                throw new ArgumentException(
                    $"LineNumber must be positive (got {line.LineNumber}).", nameof(req));

            if (!seenLineNumbers.Add(line.LineNumber))
                throw new ArgumentException(
                    $"Duplicate LineNumber {line.LineNumber}.", nameof(req));

            if (line.ReceivedQuantity <= 0)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: ReceivedQuantity must be positive.", nameof(req));

            if (line.ProductId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: ProductId is required.", nameof(req));

            if (line.UomId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: UomId is required.", nameof(req));

            if (line.OwnerId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: OwnerId is required.", nameof(req));

            if (line.LocationId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: LocationId is required.", nameof(req));
        }
    }
}
