using WMS.DAL.Common;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14E — tenant-scoped persistence for outbound.Shipments. Same
// shape as Phase 14D PackTaskRepository.
public interface IShipmentRepository
{
    // INSERT a single shipment. Caller pre-assigns Id + ShipmentNumber.
    // Header lands as 'Pending' via DB default. GeneratedAt populates
    // server-side. CarrierName + TrackingNumber + Notes are nullable
    // and may be filled in later via UpdateOnSubmitAsync.
    Task CreateAsync(
        Shipment header, Guid? userId, CancellationToken ct = default);

    Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Shipment?> GetByNumberAsync(
        string shipmentNumber, CancellationToken ct = default);

    // Phase 14E — pre-generation guard. One Active (Pending) shipment
    // per SO. Returns null when no active shipment exists.
    Task<Shipment?> GetActiveBySalesOrderAsync(
        Guid salesOrderId, CancellationToken ct = default);

    // ----- State transitions -----
    // All idempotent at SQL level via WHERE Status='Pending' filter.

    // Pending → Shipped + stamp CarrierName + TrackingNumber + Notes.
    // ShippedAt/By populated server-side. Single UPDATE.
    Task<bool> SetShippedAsync(
        Guid shipmentId,
        string? carrierName,
        string? trackingNumber,
        string? notes,
        Guid? userId,
        CancellationToken ct = default);

    // Pending → Cancelled with required reason. Idempotent.
    Task<bool> SetCancelledAsync(
        Guid shipmentId, string reason, Guid? userId,
        CancellationToken ct = default);

    // SHP-YYYYMMDD-NNNN — count of shipments with ShipmentNumber LIKE
    // prefix.
    Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default);

    // ----- Phase 15A — list-page reads -----

    Task<PagedResult<ShipmentListRow>> GetPagedAsync(
        ShipmentFilter filter, CancellationToken ct = default);

    Task<ShipmentStatusCounts> GetStatusCountsAsync(
        ShipmentFilter filter, CancellationToken ct = default);
}

public interface IShipmentRepositoryFactory
{
    IShipmentRepository For(Guid tenantId);
}
