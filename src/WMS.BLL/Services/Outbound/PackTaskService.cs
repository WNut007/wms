using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14D — pack task orchestration. T4 ships GenerateAsync only;
// SubmitAsync (T5) and CancelAsync (T6) plug onto this service.
//
// Generate is the lightest of the three lifecycle methods: no Stock
// writes, no SO state flip (SO stays Picked|PartiallyPicked while
// pack is in flight). Just inserts the task header + lines snapshotted
// from the SO's positively-picked lines.
public sealed class PackTaskService : IPackTaskService
{
    private readonly ISalesOrderRepositoryFactory _soRepoFactory;
    private readonly IPackTaskRepositoryFactory _packRepoFactory;
    private readonly ICartonRepositoryFactory _cartonRepoFactory;
    private readonly ILogger<PackTaskService> _logger;

    public PackTaskService(
        ISalesOrderRepositoryFactory soRepoFactory,
        IPackTaskRepositoryFactory packRepoFactory,
        ICartonRepositoryFactory cartonRepoFactory,
        ILogger<PackTaskService> logger)
    {
        _soRepoFactory = soRepoFactory;
        _packRepoFactory = packRepoFactory;
        _cartonRepoFactory = cartonRepoFactory;
        _logger = logger;
    }

    public async Task<PackTaskGenerationResult> GenerateAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var soRepo = _soRepoFactory.For(tenantId);
        var packRepo = _packRepoFactory.For(tenantId);

        var detail = await soRepo.GetByIdAsync(salesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {salesOrderId} not found.");

        var status = detail.Header.Status;

        // Idempotent on existing Pending pack task — return its summary.
        // Common case: operator double-clicks Generate; the controller
        // redirects to the same Detail either way.
        var existing = await packRepo.GetActiveBySalesOrderAsync(salesOrderId, ct);
        if (existing is not null)
        {
            var existingDetail = await packRepo.GetByIdAsync(existing.Id, ct)
                ?? throw new InvalidOperationException(
                    $"Pack task {existing.Id} disappeared between lookups.");
            return new PackTaskGenerationResult(
                existing.Id,
                existing.PackNumber,
                existingDetail.Lines.Count,
                existingDetail.Lines.Sum(l => l.PickedQuantity));
        }

        if (status is not "Picked" and not "PartiallyPicked")
            throw new InvalidOperationException(
                $"Cannot generate pack task for SO in '{status}' state — only Picked or PartiallyPicked allowed.");

        // Snapshot only positively-picked lines (PickedQuantity > 0).
        // Lines that were Skipped on pick or got zero pick don't enter
        // the carton — nothing to pack.
        var packableLines = detail.Lines
            .Where(l => l.PickedQuantity > 0m)
            .OrderBy(l => l.LineNumber)
            .ToList();

        if (packableLines.Count == 0)
            throw new InvalidOperationException(
                $"SO {salesOrderId} has no positively-picked lines to pack.");

        var packTaskId = Guid.NewGuid();
        var lineNumber = 1;
        var lines = packableLines.Select(soLine => new PackTaskLine
        {
            Id = Guid.NewGuid(),
            PackTaskId = packTaskId,
            LineNumber = lineNumber++,
            SalesOrderLineId = soLine.Id,
            ProductId = soLine.ProductId,
            OwnerId = soLine.OwnerId,
            UomId = soLine.UomId,
            PickedQuantity = soLine.PickedQuantity,
            LineStatus = "Pending",
        }).ToList();

        var datePrefix = $"PACK-{DateTime.UtcNow:yyyyMMdd}-";
        var existingCount = await packRepo.CountForDatePrefixAsync(datePrefix, ct);
        var packNumber = $"{datePrefix}{(existingCount + 1):D4}";

        var header = new PackTask
        {
            Id = packTaskId,
            PackNumber = packNumber,
            SalesOrderId = salesOrderId,
            Status = "Pending",
            Notes = null,
        };

        // No SO state flip — the SO stays Picked|PartiallyPicked while
        // pack is in flight. Single-repo insert; no TX needed.
        await packRepo.CreateAsync(header, lines, currentUserId, ct);

        var totalPicked = lines.Sum(l => l.PickedQuantity);
        _logger.LogInformation(
            "Generated pack task {PackNumber} ({PackId}) for SO {SoNumber} ({SoId}) — {LineCount} lines, total picked {Total}",
            packNumber, packTaskId, detail.Header.SoNumber, salesOrderId, lines.Count, totalPicked);

        return new PackTaskGenerationResult(
            packTaskId, packNumber, lines.Count, totalPicked);
    }
}
