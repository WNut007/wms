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
}
