using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14E — shipment orchestration. T4 ships GenerateAsync only;
// SubmitAsync (T5) and CancelAsync (T6) plug onto this service.
//
// Generate is the lightest of the three lifecycle methods: no Stock
// writes, no SO state flip, no carton stamping. Just inserts the
// Shipment row in Pending state.
public sealed class ShipmentService : IShipmentService
{
    private readonly ISalesOrderRepositoryFactory _soRepoFactory;
    private readonly IShipmentRepositoryFactory _shipmentRepoFactory;
    private readonly ICartonRepositoryFactory _cartonRepoFactory;
    private readonly ILogger<ShipmentService> _logger;

    public ShipmentService(
        ISalesOrderRepositoryFactory soRepoFactory,
        IShipmentRepositoryFactory shipmentRepoFactory,
        ICartonRepositoryFactory cartonRepoFactory,
        ILogger<ShipmentService> logger)
    {
        _soRepoFactory = soRepoFactory;
        _shipmentRepoFactory = shipmentRepoFactory;
        _cartonRepoFactory = cartonRepoFactory;
        _logger = logger;
    }

    public async Task<ShipmentGenerationResult> GenerateAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var soRepo = _soRepoFactory.For(tenantId);
        var shipmentRepo = _shipmentRepoFactory.For(tenantId);

        var detail = await soRepo.GetByIdAsync(salesOrderId, ct)
            ?? throw new InvalidOperationException(
                $"SalesOrder {salesOrderId} not found.");

        // Idempotent on existing Pending shipment — return its summary.
        // Common case: operator double-clicks Generate; the controller
        // redirects to the same Detail either way.
        var existing = await shipmentRepo.GetActiveBySalesOrderAsync(salesOrderId, ct);
        if (existing is not null)
            return new ShipmentGenerationResult(existing.Id, existing.ShipmentNumber);

        if (detail.Header.Status != "Packed")
            throw new InvalidOperationException(
                $"Cannot generate shipment for SO in '{detail.Header.Status}' state — only Packed allowed.");

        var datePrefix = $"SHP-{DateTime.UtcNow:yyyyMMdd}-";
        var existingCount = await shipmentRepo.CountForDatePrefixAsync(datePrefix, ct);
        var shipmentNumber = $"{datePrefix}{(existingCount + 1):D4}";

        var shipmentId = Guid.NewGuid();
        var header = new Shipment
        {
            Id = shipmentId,
            ShipmentNumber = shipmentNumber,
            SalesOrderId = salesOrderId,
            Status = "Pending",
        };

        // No SO state flip — the SO stays Packed while ship is in
        // flight. Single-repo insert; no TX needed.
        await shipmentRepo.CreateAsync(header, currentUserId, ct);

        _logger.LogInformation(
            "Generated shipment {ShipmentNumber} ({ShipmentId}) for SO {SoNumber} ({SoId})",
            shipmentNumber, shipmentId, detail.Header.SoNumber, salesOrderId);

        return new ShipmentGenerationResult(shipmentId, shipmentNumber);
    }
}
