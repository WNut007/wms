using System.Data;
using Dapper;
using WMS.DAL.Common;
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

    public async Task<PagedResult<ShipmentListRow>> GetPagedAsync(
        ShipmentFilter f, CancellationToken ct = default)
    {
        var orderBy = ShipmentSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip = (f.Page - 1) * f.PageSize;
        var take = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        // Search matches ShipmentNumber, SoNumber, AND TrackingNumber —
        // operators look up shipments by any of these (e.g. customer
        // calls with a tracking number, ops needs to find the SO).
        const string whereClause = @"
WHERE (@Status     IS NULL OR s.Status = @Status)
  AND (@SearchLike IS NULL
       OR s.ShipmentNumber LIKE @SearchLike
       OR so.SoNumber      LIKE @SearchLike
       OR s.TrackingNumber LIKE @SearchLike)";

        var sql = $@"
WITH agg AS (
    SELECT ShipmentId, COUNT(*) AS CartonCount
    FROM outbound.Cartons
    WHERE ShipmentId IS NOT NULL
    GROUP BY ShipmentId
)
SELECT
    s.Id, s.ShipmentNumber,
    s.SalesOrderId, so.SoNumber,
    c.Code AS CustomerCode, c.Name AS CustomerName,
    s.Status,
    s.CarrierName,
    s.TrackingNumber,
    ISNULL(agg.CartonCount, 0) AS CartonCount,
    s.GeneratedAt,
    COALESCE(u.FullName, u.Email, 'System') AS GeneratedByName,
    s.ShippedAt,
    s.CancelledAt
FROM outbound.Shipments s
JOIN outbound.SalesOrders so ON so.Id = s.SalesOrderId
JOIN master.Customers     c  ON c.Id  = so.CustomerId
LEFT JOIN security.Users  u  ON u.Id  = s.GeneratedBy
LEFT JOIN agg ON agg.ShipmentId = s.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM outbound.Shipments s
JOIN outbound.SalesOrders so ON so.Id = s.SalesOrderId
{whereClause};";

        var args = new
        {
            f.Status,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<ShipmentListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<ShipmentListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<ShipmentStatusCounts> GetStatusCountsAsync(
        ShipmentFilter f, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                  AS [All],
    SUM(CASE WHEN s.Status = 'Pending'   THEN 1 ELSE 0 END)  AS Pending,
    SUM(CASE WHEN s.Status = 'Shipped'   THEN 1 ELSE 0 END)  AS Shipped,
    SUM(CASE WHEN s.Status = 'Cancelled' THEN 1 ELSE 0 END)  AS Cancelled
FROM outbound.Shipments s
JOIN outbound.SalesOrders so ON so.Id = s.SalesOrderId
WHERE (@SearchLike IS NULL
       OR s.ShipmentNumber LIKE @SearchLike
       OR so.SoNumber      LIKE @SearchLike
       OR s.TrackingNumber LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<ShipmentStatusCounts>(
            new CommandDefinition(sql, new { SearchLike = searchLike }, cancellationToken: ct));
    }
}
