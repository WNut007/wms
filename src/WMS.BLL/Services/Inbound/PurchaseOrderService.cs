using System.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Inbound;
using WMS.Domain.Entities.Inbound;

namespace WMS.BLL.Services.Inbound;

// Validates the request shape (the database CHECK constraints catch
// most of the rest), materialises Domain entities with fresh Ids,
// delegates the multi-row insert to the repo, then re-fetches the
// resulting Detail so callers see DB-stamped timestamps.
public sealed class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IPurchaseOrderRepositoryFactory _repoFactory;
    private readonly ILogger<PurchaseOrderService> _logger;

    public PurchaseOrderService(
        IPurchaseOrderRepositoryFactory repoFactory,
        ILogger<PurchaseOrderService> logger)
    {
        _repoFactory = repoFactory;
        _logger = logger;
    }

    public async Task<PurchaseOrderDetail> CreateAsync(
        Guid tenantId,
        CreatePurchaseOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        Validate(request);

        var headerId = Guid.NewGuid();
        var header = new PurchaseOrder
        {
            Id = headerId,
            PoNumber = request.PoNumber,
            OwnerId = request.OwnerId,
            WarehouseId = request.WarehouseId,
            ExpectedDate = request.ExpectedDate,
            Notes = request.Notes,
            Status = "Open",
        };

        var lines = request.Lines
            .Select(l => new PurchaseOrderLine
            {
                Id = Guid.NewGuid(),
                PurchaseOrderId = headerId,
                LineNumber = l.LineNumber,
                ProductId = l.ProductId,
                UomId = l.UomId,
                ExpectedQuantity = l.ExpectedQuantity,
                ReceivedQuantity = 0m,
                Status = "Open",
            })
            .ToList();

        var repo = _repoFactory.For(tenantId);
        await repo.CreateAsync(header, lines, currentUserId, ct);

        var detail = await repo.GetByIdAsync(headerId, ct)
            ?? throw new InvalidOperationException(
                $"PurchaseOrder {headerId} not found immediately after create.");

        _logger.LogInformation(
            "Created PO {PoNumber} ({PoId}) with {LineCount} line(s) for owner {OwnerId} at warehouse {WarehouseId}",
            request.PoNumber, headerId, lines.Count, request.OwnerId, request.WarehouseId);

        return detail;
    }

    public Task<PurchaseOrderDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByIdAsync(id, ct);

    public Task<PurchaseOrderDetail?> GetByNumberAsync(
        Guid tenantId, string poNumber, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByNumberAsync(poNumber, ct);

    // ====================================================================
    // Phase 9A — Update / Archive / Status transitions
    // ====================================================================

    public async Task<PurchaseOrderDetail> UpdateAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        UpdatePurchaseOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);

        var existing = await repo.GetByIdAsync(purchaseOrderId, ct)
            ?? throw new InvalidOperationException(
                $"PurchaseOrder {purchaseOrderId} not found.");

        // Update header always allowed. Line replacement gated on
        // "no receipts have bumped any line yet" — once any line has
        // ReceivedQty > 0, lines are read-only until Phase 10 ships
        // receipt-aware line editing.
        if (request.ReplaceLines)
        {
            ValidateLines(request.Lines);
            var receivedLineCount = await repo.CountReceivedLinesAsync(purchaseOrderId, ct);
            if (receivedLineCount > 0)
            {
                throw new InvalidOperationException(
                    "Cannot replace lines — receipts have already been posted against this PO. " +
                    "Cancel and recreate to change line shape.");
            }
        }

        var headerEntity = new PurchaseOrder
        {
            Id = purchaseOrderId,
            PoNumber = existing.Header.PoNumber,        // frozen
            OwnerId = existing.Header.OwnerId,           // frozen
            WarehouseId = existing.Header.WarehouseId,   // frozen
            ExpectedDate = request.ExpectedDate,
            Notes = request.Notes,
            Status = existing.Header.Status,             // unchanged here
        };

        var ok = await repo.UpdateHeaderAsync(headerEntity, currentUserId, ct);
        if (!ok)
            throw new InvalidOperationException(
                $"Failed to update PurchaseOrder {purchaseOrderId} header.");

        if (request.ReplaceLines)
        {
            var newLines = request.Lines
                .Select(l => new PurchaseOrderLine
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = purchaseOrderId,
                    LineNumber = l.LineNumber,
                    ProductId = l.ProductId,
                    UomId = l.UomId,
                    ExpectedQuantity = l.ExpectedQuantity,
                    ReceivedQuantity = 0m,
                    Status = "Open",
                })
                .ToList();
            await repo.ReplaceLinesAsync(purchaseOrderId, newLines, currentUserId, ct);
        }

        var detail = await repo.GetByIdAsync(purchaseOrderId, ct)
            ?? throw new InvalidOperationException(
                $"PurchaseOrder {purchaseOrderId} not found after update.");

        _logger.LogInformation(
            "Updated PO {PoNumber} ({PoId}) — replaceLines={ReplaceLines}",
            detail.Header.PoNumber, purchaseOrderId, request.ReplaceLines);

        return detail;
    }

    // ====================================================================
    // d.2.3.a — surgical partial update (TD-026 closure prep)
    // ====================================================================

    public async Task<PurchaseOrderDetail> UpdatePartialAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        PartialUpdatePurchaseOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);

        var existing = await repo.GetByIdAsync(purchaseOrderId, ct)
            ?? throw new InvalidOperationException(
                $"PurchaseOrder {purchaseOrderId} not found.");

        // Closed/Cancelled POs are terminal — controller redirects to
        // Detail before reaching here; this is the defence-in-depth
        // guard against direct service callers (background jobs, tests,
        // a future API surface).
        if (existing.Header.Status is "Closed" or "Cancelled")
            throw new InvalidOperationException(
                $"Cannot edit PO {existing.Header.PoNumber}: status is " +
                $"{existing.Header.Status}.");

        // Authoritative lookup. DB state — not the request — drives
        // locked-vs-unlocked decisions.
        var dbLinesById = existing.Lines.ToDictionary(l => l.Id);

        // ---- Validate updates ----
        foreach (var u in request.LineUpdates)
        {
            if (!dbLinesById.TryGetValue(u.LineId, out var dbLine))
                throw new InvalidOperationException(
                    $"Cannot update line {u.LineId}: not found on PO " +
                    $"{existing.Header.PoNumber}.");
            if (dbLine.ReceivedQuantity > 0)
                throw new InvalidOperationException(
                    $"Cannot edit line {dbLine.LineNumber}: receipts " +
                    $"posted (qty {dbLine.ReceivedQuantity}).");
            if (u.ProductId == Guid.Empty)
                throw new InvalidOperationException(
                    $"Line {dbLine.LineNumber}: ProductId is required.");
            if (u.UomId == Guid.Empty)
                throw new InvalidOperationException(
                    $"Line {dbLine.LineNumber}: UomId is required.");
            if (u.ExpectedQuantity <= 0)
                throw new InvalidOperationException(
                    $"Line {dbLine.LineNumber}: ExpectedQuantity must be positive.");
        }

        // ---- Validate deletes ----
        foreach (var deleteId in request.LineDeletes)
        {
            if (!dbLinesById.TryGetValue(deleteId, out var dbLine))
                throw new InvalidOperationException(
                    $"Cannot delete line {deleteId}: not found on PO " +
                    $"{existing.Header.PoNumber}.");
            if (dbLine.ReceivedQuantity > 0)
                throw new InvalidOperationException(
                    $"Cannot delete line {dbLine.LineNumber}: receipts " +
                    $"posted (qty {dbLine.ReceivedQuantity}). Cancel-and-" +
                    $"recreate the PO to remove this line.");
        }

        // ---- Validate inserts ----
        var existingLineNumbers = existing.Lines
            .Select(l => l.LineNumber).ToHashSet();
        var insertLineNumbers = new HashSet<int>();
        foreach (var i in request.LineInserts)
        {
            if (i.LineNumber <= 0)
                throw new InvalidOperationException(
                    $"LineNumber must be positive (got {i.LineNumber}).");
            if (existingLineNumbers.Contains(i.LineNumber))
                throw new InvalidOperationException(
                    $"LineNumber {i.LineNumber} already exists on this PO.");
            if (!insertLineNumbers.Add(i.LineNumber))
                throw new InvalidOperationException(
                    $"Duplicate LineNumber {i.LineNumber} in insert batch.");
            if (i.ProductId == Guid.Empty)
                throw new InvalidOperationException(
                    $"Insert line {i.LineNumber}: ProductId is required.");
            if (i.UomId == Guid.Empty)
                throw new InvalidOperationException(
                    $"Insert line {i.LineNumber}: UomId is required.");
            if (i.ExpectedQuantity <= 0)
                throw new InvalidOperationException(
                    $"Insert line {i.LineNumber}: ExpectedQuantity must be positive.");
        }

        // ---- Atomic apply ----
        // TransactionScope spans the multi-connection batch (header +
        // per-line ops). MSDTC promotion accepted per TD-022; Phase
        // 10B / 11A / 12 / 13 precedent.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        var headerEntity = new PurchaseOrder
        {
            Id = purchaseOrderId,
            PoNumber = existing.Header.PoNumber,       // frozen
            OwnerId = existing.Header.OwnerId,          // frozen
            WarehouseId = existing.Header.WarehouseId,  // frozen
            ExpectedDate = request.ExpectedDate,
            Notes = request.Notes,
            Status = existing.Header.Status,            // unchanged here
        };
        var ok = await repo.UpdateHeaderAsync(headerEntity, currentUserId, ct);
        if (!ok)
            throw new InvalidOperationException(
                $"Failed to update PurchaseOrder {purchaseOrderId} header.");

        foreach (var u in request.LineUpdates)
        {
            await repo.UpdateLineAsync(
                u.LineId, u.ProductId, u.UomId, u.ExpectedQuantity,
                currentUserId, ct);
        }

        foreach (var i in request.LineInserts)
        {
            await repo.InsertSingleLineAsync(
                purchaseOrderId, i.LineNumber, i.ProductId, i.UomId,
                i.ExpectedQuantity, currentUserId, ct);
        }

        foreach (var deleteId in request.LineDeletes)
        {
            try
            {
                await repo.DeleteLineAsync(deleteId, ct);
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                // In-flight race: a receipt landed against this line
                // after our pre-check but before the DELETE landed.
                // FK NO ACTION on ReceivingLines refuses. Convert to
                // a friendly InvalidOpEx so the controller can surface
                // it to the operator.
                var dbLine = dbLinesById[deleteId];
                throw new InvalidOperationException(
                    $"Cannot delete line {dbLine.LineNumber}: a receipt " +
                    $"landed against it during this edit. Refresh and retry.",
                    ex);
            }
        }

        scope.Complete();

        var detail = await repo.GetByIdAsync(purchaseOrderId, ct)
            ?? throw new InvalidOperationException(
                $"PurchaseOrder {purchaseOrderId} not found after partial update.");

        _logger.LogInformation(
            "Partial-updated PO {PoNumber} ({PoId}) — {UpdateCount} update(s), {InsertCount} insert(s), {DeleteCount} delete(s)",
            detail.Header.PoNumber, purchaseOrderId,
            request.LineUpdates.Count, request.LineInserts.Count, request.LineDeletes.Count);

        return detail;
    }

    public async Task<bool> ArchiveAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);

        // Two valid source states: Open or Receiving. Try Open first,
        // then Receiving. SetStatusAsync's WHERE Status=@from filter
        // makes both attempts cheap and ordering-safe.
        var changed =
            await repo.SetStatusAsync(purchaseOrderId, "Open", "Cancelled", currentUserId, ct)
         || await repo.SetStatusAsync(purchaseOrderId, "Receiving", "Cancelled", currentUserId, ct);

        if (!changed) return false;

        // Cascade to lines.
        await repo.CancelOpenLinesAsync(purchaseOrderId, currentUserId, ct);

        _logger.LogInformation(
            "Archived (cancelled) PO {PoId} — child lines cascaded",
            purchaseOrderId);

        return true;
    }

    public async Task<bool> MarkReceivingAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);
        var changed = await repo.SetStatusAsync(
            purchaseOrderId, "Open", "Receiving", currentUserId, ct);

        if (changed)
        {
            _logger.LogInformation(
                "PO {PoId} transitioned Open → Receiving",
                purchaseOrderId);
        }
        return changed;
    }

    public async Task<bool> MarkClosedAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);

        // Only close when every non-Cancelled line is fully received.
        var ready = await repo.AllLinesFullyReceivedAsync(purchaseOrderId, ct);
        if (!ready) return false;

        // Idempotent — accept either Open (rare; single-receipt PO that
        // skips Receiving) or Receiving as the source state.
        var changed =
            await repo.SetStatusAsync(purchaseOrderId, "Receiving", "Closed", currentUserId, ct)
         || await repo.SetStatusAsync(purchaseOrderId, "Open", "Closed", currentUserId, ct);

        if (changed)
        {
            _logger.LogInformation(
                "PO {PoId} transitioned to Closed — all lines fully received",
                purchaseOrderId);
        }
        return changed;
    }

    public async Task<bool> RevertStatusAfterCancelAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);

        // Cancellation may have rolled some lines below their close
        // threshold; if so we step back from Closed → Receiving (or
        // → Open if no receipts remain). If receipts on this PO still
        // satisfy AllLinesFullyReceived (= the cancelled receipt was
        // for a different PO and somehow we're called), no-op.
        if (await repo.AllLinesFullyReceivedAsync(purchaseOrderId, ct))
            return false;

        var anyReceived = await repo.AnyLineHasReceiptsAsync(purchaseOrderId, ct);
        var target = anyReceived ? "Receiving" : "Open";

        // Try every non-Cancelled source state. SetStatusAsync's
        // WHERE Status=@from filter makes each attempt cheap and the
        // sequence picks whichever applies. Cancelled POs (user-
        // archived) are never touched — RevertStatus on a Cancelled
        // PO is a no-op by exclusion.
        var changed =
               await repo.SetStatusAsync(purchaseOrderId, "Closed",    target, currentUserId, ct)
            || await repo.SetStatusAsync(purchaseOrderId, "Receiving", target, currentUserId, ct);

        if (changed)
        {
            _logger.LogInformation(
                "PO {PoId} reverted to {Target} after receipt cancellation",
                purchaseOrderId, target);
        }
        return changed;
    }

    // ====================================================================
    // Validation
    // ====================================================================

    private static void Validate(CreatePurchaseOrderRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PoNumber))
            throw new ArgumentException("PoNumber is required.", nameof(req));

        if (req.OwnerId == Guid.Empty)
            throw new ArgumentException("OwnerId is required.", nameof(req));

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

            if (line.ExpectedQuantity <= 0)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: ExpectedQuantity must be positive.", nameof(req));

            if (line.ProductId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: ProductId is required.", nameof(req));

            if (line.UomId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: UomId is required.", nameof(req));
        }
    }

    private static void ValidateLines(IReadOnlyList<UpdatePurchaseOrderLineRequest> lines)
    {
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("At least one line is required.");

        var seen = new HashSet<int>();
        foreach (var line in lines)
        {
            if (line.LineNumber <= 0)
                throw new ArgumentException($"LineNumber must be positive (got {line.LineNumber}).");
            if (!seen.Add(line.LineNumber))
                throw new ArgumentException($"Duplicate LineNumber {line.LineNumber}.");
            if (line.ExpectedQuantity <= 0)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: ExpectedQuantity must be positive.");
            if (line.ProductId == Guid.Empty)
                throw new ArgumentException($"Line {line.LineNumber}: ProductId is required.");
            if (line.UomId == Guid.Empty)
                throw new ArgumentException($"Line {line.LineNumber}: UomId is required.");
        }
    }
}
