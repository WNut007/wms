using System.Data;
using Dapper;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

internal sealed class ShipmentRepository : IShipmentRepository
{
    private const string Columns = @"
        Id, ShipmentNumber, SalesOrderId, Status,
        CarrierName, TrackingNumber, Notes,
        GeneratedAt, GeneratedBy,
        ShippedAt, ShippedBy,
        CancelledAt, CancelledBy, CancelReason,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM outbound.Shipments";

    private readonly IDbConnection _connection;

    public ShipmentRepository(IDbConnection connection) => _connection = connection;

    public Task CreateAsync(
        Shipment h, Guid? userId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO outbound.Shipments
                  (Id, ShipmentNumber, SalesOrderId, Status,
                   CarrierName, TrackingNumber, Notes,
                   GeneratedBy, CreatedBy)
              VALUES
                  (@Id, @ShipmentNumber, @SalesOrderId, @Status,
                   @CarrierName, @TrackingNumber, @Notes,
                   @UserId, @UserId);",
            new
            {
                h.Id, h.ShipmentNumber, h.SalesOrderId, h.Status,
                h.CarrierName, h.TrackingNumber, h.Notes,
                UserId = userId,
            },
            cancellationToken: ct));

    public Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Shipment?>(new CommandDefinition(
            "SELECT " + Columns + " WHERE Id = @id;",
            new { id }, cancellationToken: ct));

    public Task<Shipment?> GetByNumberAsync(
        string shipmentNumber, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Shipment?>(new CommandDefinition(
            "SELECT " + Columns + " WHERE ShipmentNumber = @shipmentNumber;",
            new { shipmentNumber }, cancellationToken: ct));

    public async Task<Shipment?> GetActiveBySalesOrderAsync(
        Guid salesOrderId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT TOP (1) " + Columns + @"
WHERE SalesOrderId = @soId
  AND Status = 'Pending'
ORDER BY GeneratedAt DESC;";

        return await _connection.QuerySingleOrDefaultAsync<Shipment?>(
            new CommandDefinition(sql, new { soId = salesOrderId }, cancellationToken: ct));
    }

    public async Task<bool> SetShippedAsync(
        Guid shipmentId,
        string? carrierName,
        string? trackingNumber,
        string? notes,
        Guid? userId,
        CancellationToken ct = default)
    {
        // Pending → Shipped. Stamp dispatch metadata + audit in one
        // UPDATE. CK_Shipments_AuditMatchesStatus requires ShippedAt
        // populated + CancelledAt NULL on this branch.
        const string sql = @"
UPDATE outbound.Shipments
SET Status         = 'Shipped',
    CarrierName    = @CarrierName,
    TrackingNumber = @TrackingNumber,
    Notes          = COALESCE(@Notes, Notes),
    ShippedAt      = SYSUTCDATETIME(),
    ShippedBy      = @UserId,
    UpdatedAt      = SYSUTCDATETIME(),
    UpdatedBy      = @UserId,
    Version        = Version + 1
WHERE Id = @Id AND Status = 'Pending';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = shipmentId,
                CarrierName = carrierName,
                TrackingNumber = trackingNumber,
                Notes = notes,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetCancelledAsync(
        Guid shipmentId, string reason, Guid? userId,
        CancellationToken ct = default)
    {
        const string sql = @"
UPDATE outbound.Shipments
SET Status       = 'Cancelled',
    CancelledAt  = SYSUTCDATETIME(),
    CancelledBy  = @UserId,
    CancelReason = @Reason,
    UpdatedAt    = SYSUTCDATETIME(),
    UpdatedBy    = @UserId,
    Version      = Version + 1
WHERE Id = @Id AND Status = 'Pending';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = shipmentId, Reason = reason, UserId = userId },
            cancellationToken: ct));
        return rows > 0;
    }

    public Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM outbound.Shipments
              WHERE ShipmentNumber LIKE @prefix + '%';",
            new { prefix = datePrefix },
            cancellationToken: ct));
}
