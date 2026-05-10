using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14A — SalesOrder workflow service.
//
// Validation up-front on Create + Update; database CHECKs catch the
// rest at insert time. Number assignment server-side via repo's
// CountForDatePrefix — same pattern as Adjustment / CycleCount /
// Transfer.
//
// Phase 14B — CancelAsync now reversal-aware: Cancelled-from-
// Allocating/Allocated releases every Active OrderAllocation +
// decrements Stock.QuantityAllocated + resets line.AllocatedQuantity
// inside one TransactionScope before flipping status. Allocation
// reversal mechanics live in IAllocationService.
public sealed class SalesOrderService : ISalesOrderService
{
    private readonly ISalesOrderRepositoryFactory _repoFactory;
    private readonly IAllocationService _allocationService;
    private readonly ILogger<SalesOrderService> _logger;

    public SalesOrderService(
        ISalesOrderRepositoryFactory repoFactory,
        IAllocationService allocationService,
        ILogger<SalesOrderService> logger)
    {
        _repoFactory = repoFactory;
        _allocationService = allocationService;
        _logger = logger;
    }

    public async Task<SalesOrderDetail> CreateAsync(
        Guid tenantId,
        CreateSalesOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        Validate(request);

        var repo = _repoFactory.For(tenantId);

        var datePrefix = $"SO-{DateTime.UtcNow:yyyyMMdd}-";
        var existing = await repo.CountForDatePrefixAsync(datePrefix, ct);
        var soNumber = $"{datePrefix}{(existing + 1):D4}";

        var headerId = Guid.NewGuid();
        var header = new SalesOrder
        {
            Id = headerId,
            SoNumber = soNumber,
            CustomerId = request.CustomerId,
            WarehouseId = request.WarehouseId,
            OrderDate = request.OrderDate,
            RequestedShipDate = request.RequestedShipDate,
            Notes = request.Notes,
            Status = "Draft",
        };

        var lines = request.Lines
            .Select(l => new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                SalesOrderId = headerId,
                LineNumber = l.LineNumber,
                ProductId = l.ProductId,
                OwnerId = l.OwnerId,
                UomId = l.UomId,
                OrderedQuantity = l.OrderedQuantity,
                UnitPrice = l.UnitPrice,
                Notes = l.Notes,
            })
            .ToList();

        await repo.CreateAsync(header, lines, currentUserId, ct);

        var detail = await repo.GetByIdAsync(headerId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {headerId} not found immediately after create.");

        _logger.LogInformation(
            "Created SO {SoNumber} ({SoId}) — {LineCount} lines for customer {CustomerId} at warehouse {WarehouseId}",
            soNumber, headerId, lines.Count, request.CustomerId, request.WarehouseId);

        return detail;
    }

    public Task<SalesOrderDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByIdAsync(id, ct);

    public Task<SalesOrderDetail?> GetByNumberAsync(
        Guid tenantId, string soNumber, CancellationToken ct = default) =>
        _repoFactory.For(tenantId).GetByNumberAsync(soNumber, ct);

    public async Task<SalesOrderDetail> UpdateAsync(
        Guid tenantId,
        Guid salesOrderId,
        UpdateSalesOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);

        var existing = await repo.GetByIdAsync(salesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {salesOrderId} not found.");

        if (existing.Header.Status == "Cancelled")
            throw new InvalidOperationException(
                "Cannot update a cancelled SO.");

        if (request.ReplaceLines)
        {
            ValidateLines(request.Lines);
            if (existing.Header.Status != "Draft")
            {
                throw new InvalidOperationException(
                    "Cannot replace lines — SO is not in Draft status. " +
                    "Cancel and recreate, or wait for Phase 14B's allocation-aware line edit.");
            }
        }

        var headerEntity = new SalesOrder
        {
            Id = salesOrderId,
            SoNumber = existing.Header.SoNumber,         // frozen
            CustomerId = existing.Header.CustomerId,     // frozen
            WarehouseId = existing.Header.WarehouseId,   // frozen
            OrderDate = request.OrderDate,
            RequestedShipDate = request.RequestedShipDate,
            Notes = request.Notes,
            Status = existing.Header.Status,             // unchanged here
        };

        var ok = await repo.UpdateHeaderAsync(headerEntity, currentUserId, ct);
        if (!ok)
            throw new InvalidOperationException(
                $"Failed to update SalesOrder {salesOrderId} header.");

        if (request.ReplaceLines)
        {
            var newLines = request.Lines
                .Select(l => new SalesOrderLine
                {
                    Id = Guid.NewGuid(),
                    SalesOrderId = salesOrderId,
                    LineNumber = l.LineNumber,
                    ProductId = l.ProductId,
                    OwnerId = l.OwnerId,
                    UomId = l.UomId,
                    OrderedQuantity = l.OrderedQuantity,
                    UnitPrice = l.UnitPrice,
                    Notes = l.Notes,
                })
                .ToList();
            await repo.ReplaceLinesAsync(salesOrderId, newLines, currentUserId, ct);
        }

        var detail = await repo.GetByIdAsync(salesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {salesOrderId} not found after update.");

        _logger.LogInformation(
            "Updated SO {SoNumber} ({SoId}) — replaceLines={ReplaceLines}",
            detail.Header.SoNumber, salesOrderId, request.ReplaceLines);

        return detail;
    }

    public async Task<bool> SubmitAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);
        var existing = await repo.GetByIdAsync(salesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {salesOrderId} not found.");

        if (existing.Header.Status == "Open") return false; // idempotent
        if (existing.Header.Status != "Draft")
            throw new InvalidOperationException(
                $"Cannot submit SO in '{existing.Header.Status}' state — only Draft can submit.");

        // Empty-line guard: reject Submit on a zero-line SO. The schema
        // doesn't strictly forbid it (lines are inserted post-header in
        // a single TX); operator UI prevents it but defensive check
        // catches programmatic callers.
        if (existing.Lines.Count == 0)
            throw new InvalidOperationException(
                "Cannot submit a SO with zero lines.");

        var changed = await repo.SetStatusAsync(
            salesOrderId, "Draft", "Open", currentUserId, ct);

        if (changed)
        {
            _logger.LogInformation(
                "SO {SoNumber} ({SoId}) submitted — Draft → Open",
                existing.Header.SoNumber, salesOrderId);
        }
        return changed;
    }

    public async Task<bool> CancelAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        var repo = _repoFactory.For(tenantId);
        var existing = await repo.GetByIdAsync(salesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {salesOrderId} not found.");

        if (existing.Header.Status == "Cancelled") return false; // idempotent

        var hadAllocations = existing.Header.Status is "Allocating" or "Allocated";
        var actorUserId = currentUserId ?? Guid.Empty;

        // TransactionScope wraps reversal + status flip together. Even
        // when there are no allocations to release, the wrapper keeps
        // the cancel flow uniform with Phase 11A/12/13 precedent.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        if (hadAllocations)
        {
            await _allocationService.ReleaseAllForSalesOrderAsync(
                tenantId, salesOrderId, "SO cancelled", actorUserId, ct);
        }

        // Try each non-terminal source state. SetStatusAsync's
        // WHERE Status=@from filter makes each attempt cheap and the
        // sequence picks whichever applies.
        var changed =
               await repo.SetStatusAsync(salesOrderId, "Draft",      "Cancelled", currentUserId, ct)
            || await repo.SetStatusAsync(salesOrderId, "Open",       "Cancelled", currentUserId, ct)
            || await repo.SetStatusAsync(salesOrderId, "Allocating", "Cancelled", currentUserId, ct)
            || await repo.SetStatusAsync(salesOrderId, "Allocated",  "Cancelled", currentUserId, ct);

        scope.Complete();

        if (changed)
        {
            _logger.LogInformation(
                "SO {SoNumber} ({SoId}) cancelled (hadAllocations={Had})",
                existing.Header.SoNumber, salesOrderId, hadAllocations);
        }
        return changed;
    }

    // ====================================================================
    // Validation
    // ====================================================================

    private static void Validate(CreateSalesOrderRequest req)
    {
        if (req.CustomerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(req));

        if (req.WarehouseId == Guid.Empty)
            throw new ArgumentException("WarehouseId is required.", nameof(req));

        if (req.OrderDate == default)
            throw new ArgumentException("OrderDate is required.", nameof(req));

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
            if (line.ProductId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: ProductId is required.", nameof(req));
            if (line.OwnerId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: OwnerId is required.", nameof(req));
            if (line.UomId == Guid.Empty)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: UomId is required.", nameof(req));
            if (line.OrderedQuantity <= 0m)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: OrderedQuantity must be positive.", nameof(req));
            if (line.UnitPrice is < 0m)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: UnitPrice cannot be negative.", nameof(req));
        }
    }

    private static void ValidateLines(IReadOnlyList<UpdateSalesOrderLineRequest> lines)
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
            if (line.ProductId == Guid.Empty)
                throw new ArgumentException($"Line {line.LineNumber}: ProductId is required.");
            if (line.OwnerId == Guid.Empty)
                throw new ArgumentException($"Line {line.LineNumber}: OwnerId is required.");
            if (line.UomId == Guid.Empty)
                throw new ArgumentException($"Line {line.LineNumber}: UomId is required.");
            if (line.OrderedQuantity <= 0m)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: OrderedQuantity must be positive.");
            if (line.UnitPrice is < 0m)
                throw new ArgumentException(
                    $"Line {line.LineNumber}: UnitPrice cannot be negative.");
        }
    }
}
