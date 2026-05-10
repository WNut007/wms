using System.Transactions;
using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14E — shipment orchestration. T4 ships GenerateAsync; T5
// adds SubmitAsync (TX-wrapped commit). CancelAsync arrives in T6.
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

    public async Task<ShipmentSubmissionResult> SubmitAsync(
        Guid tenantId,
        SubmitShipmentRequest request,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var shipmentRepo = _shipmentRepoFactory.For(tenantId);
        var cartonRepo = _cartonRepoFactory.For(tenantId);
        var soRepo = _soRepoFactory.For(tenantId);

        var shipment = await shipmentRepo.GetByIdAsync(request.ShipmentId, ct)
            ?? throw new InvalidOperationException(
                $"Shipment {request.ShipmentId} not found.");

        if (shipment.Status != "Pending")
            throw new InvalidOperationException(
                $"Cannot submit shipment in '{shipment.Status}' state — only Pending allowed.");

        // Trim + cap operator inputs to match column widths. Empty
        // strings normalised to null (so downstream displays render
        // "—" instead of an empty cell).
        var carrierName = string.IsNullOrWhiteSpace(request.CarrierName)
            ? null : Trunc(request.CarrierName.Trim(), 50);
        var trackingNumber = string.IsNullOrWhiteSpace(request.TrackingNumber)
            ? null : Trunc(request.TrackingNumber.Trim(), 100);
        var notes = string.IsNullOrWhiteSpace(request.Notes)
            ? null : request.Notes.Trim();

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        // 1. Shipment Pending → Shipped + stamp dispatch metadata.
        var shipFlipped = await shipmentRepo.SetShippedAsync(
            request.ShipmentId, carrierName, trackingNumber, notes,
            currentUserId, ct);
        if (!shipFlipped)
            throw new InvalidOperationException(
                $"Failed to flip shipment {request.ShipmentId} Pending→Shipped — concurrent state change?");

        // 2. Stamp ShipmentId on every carton belonging to the SO.
        var cartonCount = await cartonRepo.StampShipmentForSalesOrderAsync(
            shipment.SalesOrderId, request.ShipmentId, currentUserId, ct);

        // 3. SO Packed → Shipped.
        var soChanged = await soRepo.SetStatusAsync(
            shipment.SalesOrderId, "Packed", "Shipped", currentUserId, ct);
        if (!soChanged)
            throw new InvalidOperationException(
                $"Failed to flip SO {shipment.SalesOrderId} Packed→Shipped — concurrent state change?");

        scope.Complete();

        _logger.LogInformation(
            "Submitted shipment {ShipmentNumber} ({ShipmentId}) — carrier={Carrier} tracking={Tracking} cartons={Cartons}",
            shipment.ShipmentNumber, request.ShipmentId,
            carrierName ?? "(none)", trackingNumber ?? "(none)", cartonCount);

        return new ShipmentSubmissionResult(
            ShipmentStatus: "Shipped",
            SalesOrderStatus: "Shipped",
            CartonCount: cartonCount);
    }

    public async Task<bool> CancelAsync(
        Guid tenantId,
        Guid shipmentId,
        string reason,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancel reason is required.", nameof(reason));

        var shipmentRepo = _shipmentRepoFactory.For(tenantId);

        var shipment = await shipmentRepo.GetByIdAsync(shipmentId, ct)
            ?? throw new InvalidOperationException(
                $"Shipment {shipmentId} not found.");

        var fromStatus = shipment.Status;

        if (fromStatus == "Cancelled") return false;

        if (fromStatus == "Shipped")
            throw new InvalidOperationException(
                $"Cannot cancel shipment in 'Shipped' state — already dispatched. " +
                "Use a future return-to-stock flow to reverse a posted shipment (not yet implemented).");

        if (fromStatus != "Pending")
            throw new InvalidOperationException(
                $"Cannot cancel shipment in '{fromStatus}' state.");

        var trimmedReason = reason.Trim();
        var changed = await shipmentRepo.SetCancelledAsync(
            shipmentId, trimmedReason, currentUserId, ct);
        if (!changed)
            throw new InvalidOperationException(
                $"Failed to cancel shipment {shipmentId} from 'Pending' — concurrent state change?");

        _logger.LogInformation(
            "Cancelled shipment {ShipmentNumber} ({ShipmentId}) — reason: {Reason}",
            shipment.ShipmentNumber, shipmentId, trimmedReason);

        return true;
    }

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max);
}
