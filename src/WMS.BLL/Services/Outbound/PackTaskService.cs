using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14D — pack task orchestration. T4 ships GenerateAsync; T5
// adds SubmitAsync (TX-wrapped commit). CancelAsync arrives in T6.
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

    public async Task<PackTaskSubmissionResult> SubmitAsync(
        Guid tenantId,
        SubmitPackTaskRequest request,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var packRepo = _packRepoFactory.For(tenantId);
        var cartonRepo = _cartonRepoFactory.For(tenantId);
        var soRepo = _soRepoFactory.For(tenantId);

        var detail = await packRepo.GetByIdAsync(request.PackTaskId, ct)
            ?? throw new InvalidOperationException(
                $"PackTask {request.PackTaskId} not found.");

        if (detail.Header.Status != "Pending")
            throw new InvalidOperationException(
                $"Cannot submit pack task in '{detail.Header.Status}' state — only Pending allowed.");

        ValidateRequestShape(request, detail.Lines);

        // Per-line aggregates for the result summary.
        int fullyPackedLines = 0;
        int shortPackedLines = 0;
        int skippedLines = 0;
        decimal totalPacked = 0m;

        var entriesByLineId = request.Lines.ToDictionary(e => e.LineId);

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        // 1. Per-line UPDATE.
        foreach (var line in detail.Lines)
        {
            var entry = entriesByLineId[line.Id];
            var packedQty = entry.LineStatus == "Packed"
                ? entry.PackedQuantity!.Value
                : 0m;

            if (entry.LineStatus == "Skipped")
                skippedLines++;
            else if (packedQty < line.PickedQuantity)
                shortPackedLines++;
            else
                fullyPackedLines++;

            totalPacked += packedQty;

            await packRepo.UpdateLinePackedAsync(
                line.Id,
                entry.LineStatus == "Packed" ? entry.PackedQuantity : null,
                entry.LineStatus,
                entry.ShortPackReason,
                entry.Notes,
                currentUserId,
                ct);
        }

        // 2. Carton INSERT (1 per task for MVP).
        var datePrefix = $"CTN-{DateTime.UtcNow:yyyyMMdd}-";
        var existingCartons = await cartonRepo.CountForDatePrefixAsync(datePrefix, ct);
        var cartonNumber = $"{datePrefix}{(existingCartons + 1):D4}";
        var carton = new Carton
        {
            Id = Guid.NewGuid(),
            CartonNumber = cartonNumber,
            PackTaskId = request.PackTaskId,
            BoxTypeId = request.BoxTypeId,
            WeightKg = request.WeightKg,
            Notes = string.IsNullOrWhiteSpace(request.CartonNotes) ? null : request.CartonNotes.Trim(),
        };
        await cartonRepo.CreateAsync(carton, currentUserId, ct);

        // 3. PackTask Pending → Packed.
        var taskFlipped = await packRepo.SetPackedAsync(
            request.PackTaskId, currentUserId, ct);
        if (!taskFlipped)
            throw new InvalidOperationException(
                $"Failed to flip pack task {request.PackTaskId} Pending→Packed — concurrent state change?");

        // 4. SO Picked|PartiallyPicked → Packed.
        // Try each from-state via || chain (Phase 14B SO Cancel
        // precedent). Whichever applies wins; the other is a no-op
        // via WHERE Status=@from filter.
        var soChanged =
               await soRepo.SetStatusAsync(detail.Header.SalesOrderId, "Picked",          "Packed", currentUserId, ct)
            || await soRepo.SetStatusAsync(detail.Header.SalesOrderId, "PartiallyPicked", "Packed", currentUserId, ct);
        if (!soChanged)
            throw new InvalidOperationException(
                $"Failed to flip SO {detail.Header.SalesOrderId} → Packed — concurrent state change or wrong source state?");

        scope.Complete();

        _logger.LogInformation(
            "Submitted pack task {PackNumber} ({PackId}) — task=Packed so=Packed full={Full} short={Short} skip={Skip} totalPacked={Total} carton={Carton}",
            detail.Header.PackNumber, request.PackTaskId,
            fullyPackedLines, shortPackedLines, skippedLines, totalPacked, cartonNumber);

        return new PackTaskSubmissionResult(
            TaskStatus: "Packed",
            SalesOrderStatus: "Packed",
            FullyPackedLineCount: fullyPackedLines,
            ShortPackedLineCount: shortPackedLines,
            SkippedLineCount: skippedLines,
            TotalPackedQuantity: totalPacked,
            CartonNumber: cartonNumber);
    }

    // Verifies the request covers exactly the task's lines (no missing,
    // no extras, no duplicates), each entry's per-line shape is valid,
    // and the carton metadata is well-formed. Throws on any violation.
    private static void ValidateRequestShape(
        SubmitPackTaskRequest request,
        IReadOnlyList<PackTaskLine> taskLines)
    {
        if (request.Lines.Count == 0)
            throw new InvalidOperationException("Submission has no lines.");

        var dict = new Dictionary<Guid, PackedLineEntry>(request.Lines.Count);
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

            if (entry.LineStatus is not "Packed" and not "Skipped")
                throw new InvalidOperationException(
                    $"Line {line.Id} status '{entry.LineStatus}' invalid — must be 'Packed' or 'Skipped'.");

            if (entry.LineStatus == "Packed")
            {
                if (entry.PackedQuantity is null)
                    throw new InvalidOperationException(
                        $"Line {line.Id} marked Packed but has no PackedQuantity.");
                if (entry.PackedQuantity < 0m || entry.PackedQuantity > line.PickedQuantity)
                    throw new InvalidOperationException(
                        $"Line {line.Id} PackedQuantity {entry.PackedQuantity} outside [0, {line.PickedQuantity}].");
                if (entry.PackedQuantity < line.PickedQuantity
                    && string.IsNullOrWhiteSpace(entry.ShortPackReason))
                    throw new InvalidOperationException(
                        $"Line {line.Id} short-packed ({entry.PackedQuantity} < {line.PickedQuantity}) — ShortPackReason required.");
            }
            else // Skipped
            {
                if (entry.PackedQuantity is not null)
                    throw new InvalidOperationException(
                        $"Line {line.Id} marked Skipped must have null PackedQuantity.");
                if (string.IsNullOrWhiteSpace(entry.ShortPackReason))
                    throw new InvalidOperationException(
                        $"Line {line.Id} skipped — ShortPackReason required.");
            }
        }

        // Carton: WeightKg non-negative if supplied (CK_Cartons_Weight-
        // Kg_NonNegative also enforces at DB; this fails fast).
        if (request.WeightKg is < 0m)
            throw new InvalidOperationException(
                $"Carton WeightKg {request.WeightKg} cannot be negative.");
    }
}
