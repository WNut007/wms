using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14D — tenant-scoped persistence for outbound.Cartons. MVP
// surface is small: INSERT at SubmitAsync time + count-for-date-prefix
// helper for CTN-NNNN assignment. Reads happen via PackTaskRepository's
// GetByIdAsync 3-recordset QueryMultiple (carton aggregated alongside
// header + lines) so no GetByX read methods here.
public interface ICartonRepository
{
    // INSERT a single carton. Caller pre-assigns Id + CartonNumber.
    // Composes inside ambient TX from PackTaskService.SubmitAsync.
    Task CreateAsync(
        Carton carton, Guid? userId, CancellationToken ct = default);

    // CTN-YYYYMMDD-NNNN — count of cartons with CartonNumber LIKE prefix.
    Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default);

    // Phase 14E — bulk stamp ShipmentId on every carton belonging to
    // the SO (resolved via PackTask.SalesOrderId join). Single UPDATE.
    // Composes inside ambient TX from ShipmentService.SubmitAsync.
    // Returns count of cartons stamped (typically 1 for MVP single-
    // carton-per-task; useful for telemetry).
    Task<int> StampShipmentForSalesOrderAsync(
        Guid salesOrderId,
        Guid shipmentId,
        Guid? userId,
        CancellationToken ct = default);

    // Phase 14E — read-side for shipment Detail page. Lists every
    // carton claimed by the given shipment (sorted by CartonNumber).
    Task<IReadOnlyList<Carton>> GetByShipmentIdAsync(
        Guid shipmentId, CancellationToken ct = default);
}

public interface ICartonRepositoryFactory
{
    ICartonRepository For(Guid tenantId);
}
