using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Inbound;
using WMS.Domain.Entities.Inbound;

namespace WMS.BLL.Services.Inbound;

// Orchestrates the post-receipt flow.
//
// Phase 1 trade-off: the four resources (receiving header / lines,
// stock, lot/pallet, PO line) each have their own atomic write but
// the *combination* is sequential without a top-level transaction.
// Failure in step N leaves steps 1..N-1's effects in place. In
// practice:
//
//   * Step 1 (Create header+lines): atomic. Failure leaves nothing.
//   * Step 2 (per-line ReceiveStockAsync via β): atomic per line.
//     Failure on line K means header + all lines exist, lines 1..K-1
//     have stock + Lot/Pallet links, line K's stock is missing.
//   * Step 3 (UpdateLineInventoryRefsAsync per line): atomic.
//     Failure leaves stock created but the receiving line not yet
//     pointing at its Lot/Pallet — recoverable by re-running.
//   * Step 4 (IncrementLineReceivedQuantityAsync): atomic.
//
// Single-instance Phase 1 + admin oversight makes this acceptable;
// a TransactionScope wrapper is the next-step polish.
public sealed class ReceivingHeaderService : IReceivingHeaderService
{
    private readonly IReceivingHeaderRepositoryFactory _receivingRepoFactory;
    private readonly IPurchaseOrderRepositoryFactory _poRepoFactory;
    private readonly IReceivingService _receivingService;
    private readonly ILogger<ReceivingHeaderService> _logger;

    public ReceivingHeaderService(
        IReceivingHeaderRepositoryFactory receivingRepoFactory,
        IPurchaseOrderRepositoryFactory poRepoFactory,
        IReceivingService receivingService,
        ILogger<ReceivingHeaderService> logger)
    {
        _receivingRepoFactory = receivingRepoFactory;
        _poRepoFactory = poRepoFactory;
        _receivingService = receivingService;
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
            Status = "Posted",
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

        // Step 1 — persist header + lines atomically.
        var receivingRepo = _receivingRepoFactory.For(tenantId);
        await receivingRepo.CreateAsync(header, lines, currentUserId, ct);

        var poRepo = _poRepoFactory.For(tenantId);

        // Steps 2 → 4 — per line: stock upsert, link lot/pallet back,
        // bump PO line if linked.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var req = request.Lines[i];

            var stock = await _receivingService.ReceiveStockAsync(
                tenantId,
                new ReceiveStockRequest(
                    LocationId: req.LocationId,
                    ProductId: req.ProductId,
                    OwnerId: req.OwnerId,
                    UomId: req.UomId,
                    Quantity: req.ReceivedQuantity,
                    Lot: req.Lot,
                    Pallet: req.Pallet),
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

        // Re-fetch — gives the caller the post-update Detail, including
        // the Lot/Pallet refs the orchestration just stamped.
        var detail = await receivingRepo.GetByIdAsync(headerId, ct)
            ?? throw new InvalidOperationException(
                $"ReceivingHeader {headerId} not found immediately after post.");

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
