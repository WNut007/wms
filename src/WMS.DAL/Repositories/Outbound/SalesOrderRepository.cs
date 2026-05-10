using System.Data;
using System.Transactions;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.DAL.Common;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14A — Dapper-backed SO repo. CreateAsync + ReplaceLinesAsync
// own a TX (ambient-aware per `feedback_transactionscope_dapper.md`)
// so the service stays free of ADO.NET plumbing.
internal sealed class SalesOrderRepository : ISalesOrderRepository
{
    private const string HeaderColumns = @"
        Id, SoNumber, CustomerId, WarehouseId,
        OrderDate, RequestedShipDate,
        Status, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM outbound.SalesOrders";

    private const string LineColumns = @"
        Id, SalesOrderId, LineNumber,
        ProductId, OwnerId, UomId,
        OrderedQuantity, AllocatedQuantity, PickedQuantity, UnitPrice, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM outbound.SalesOrderLines";

    private readonly IDbConnection _connection;

    public SalesOrderRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        SalesOrder header,
        IReadOnlyList<SalesOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        // TD-022 ambient-aware pattern (Phase 10B precedent).
        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO outbound.SalesOrders
                      (Id, SoNumber, CustomerId, WarehouseId,
                       OrderDate, RequestedShipDate,
                       Status, Notes, CreatedBy)
                  VALUES
                      (@Id, @SoNumber, @CustomerId, @WarehouseId,
                       @OrderDate, @RequestedShipDate,
                       @Status, @Notes, @UserId);",
                new
                {
                    header.Id,
                    header.SoNumber,
                    header.CustomerId,
                    header.WarehouseId,
                    header.OrderDate,
                    header.RequestedShipDate,
                    header.Status,
                    header.Notes,
                    UserId = userId,
                },
                transaction: tx,
                cancellationToken: ct));

            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO outbound.SalesOrderLines
                          (Id, SalesOrderId, LineNumber,
                           ProductId, OwnerId, UomId,
                           OrderedQuantity, UnitPrice, Notes,
                           CreatedBy)
                      VALUES
                          (@Id, @SalesOrderId, @LineNumber,
                           @ProductId, @OwnerId, @UomId,
                           @OrderedQuantity, @UnitPrice, @Notes,
                           @UserId);",
                    new
                    {
                        line.Id,
                        line.SalesOrderId,
                        line.LineNumber,
                        line.ProductId,
                        line.OwnerId,
                        line.UomId,
                        line.OrderedQuantity,
                        line.UnitPrice,
                        line.Notes,
                        UserId = userId,
                    },
                    transaction: tx,
                    cancellationToken: ct));
            }

            tx?.Commit();
        }
        catch
        {
            tx?.Rollback();
            throw;
        }
    }

    public Task<SalesOrderDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE SalesOrderId = @id ORDER BY LineNumber;",
            new { id }, ct);

    public Task<SalesOrderDetail?> GetByNumberAsync(string soNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM outbound.SalesOrders WHERE SoNumber = @soNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE SalesOrderId = @id ORDER BY LineNumber;",
            new { soNumber }, ct);

    private async Task<SalesOrderDetail?> ReadDetailAsync(
        string sql, object args, CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<SalesOrder?>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLine>()).AsList();
        return new SalesOrderDetail(header, lines);
    }

    public async Task<PagedResult<SalesOrderListRow>> GetPagedAsync(
        SalesOrderFilter f, CancellationToken ct = default)
    {
        var orderBy = SalesOrderSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip = (f.Page - 1) * f.PageSize;
        var take = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@Status        IS NULL OR so.Status = @Status)
  AND (@CustomerCode  IS NULL OR c.Code    = @CustomerCode)
  AND (@WarehouseCode IS NULL OR wh.Code   = @WarehouseCode)
  AND (@SearchLike    IS NULL OR so.SoNumber LIKE @SearchLike)";

        var sql = $@"
WITH agg AS (
    SELECT SalesOrderId,
           COUNT(*) AS LineCount,
           SUM(OrderedQuantity) AS TotalQuantity
    FROM outbound.SalesOrderLines
    GROUP BY SalesOrderId
)
SELECT
    so.Id, so.SoNumber,
    so.CustomerId, c.Code  AS CustomerCode, c.Name AS CustomerName,
    so.WarehouseId, wh.Code AS WarehouseCode,
    so.OrderDate, so.RequestedShipDate,
    so.Status,
    ISNULL(agg.LineCount,     0) AS LineCount,
    ISNULL(agg.TotalQuantity, 0) AS TotalQuantity,
    COALESCE(u.FullName, u.Email, 'System') AS CreatedByName,
    so.CreatedAt
FROM outbound.SalesOrders so
JOIN master.Customers   c  ON c.Id  = so.CustomerId
JOIN master.Warehouses  wh ON wh.Id = so.WarehouseId
LEFT JOIN security.Users u ON u.Id  = so.CreatedBy
LEFT JOIN agg ON agg.SalesOrderId = so.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM outbound.SalesOrders so
JOIN master.Customers   c  ON c.Id  = so.CustomerId
JOIN master.Warehouses  wh ON wh.Id = so.WarehouseId
{whereClause};";

        var args = new
        {
            f.Status,
            f.CustomerCode,
            f.WarehouseCode,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<SalesOrderListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<SalesOrderListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<SalesOrderStatusCounts> GetStatusCountsAsync(
        SalesOrderFilter f, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                        AS [All],
    SUM(CASE WHEN so.Status = 'Draft'           THEN 1 ELSE 0 END)  AS Draft,
    SUM(CASE WHEN so.Status = 'Open'            THEN 1 ELSE 0 END)  AS [Open],
    SUM(CASE WHEN so.Status = 'Allocating'      THEN 1 ELSE 0 END)  AS Allocating,
    SUM(CASE WHEN so.Status = 'Allocated'       THEN 1 ELSE 0 END)  AS Allocated,
    SUM(CASE WHEN so.Status = 'Picking'         THEN 1 ELSE 0 END)  AS Picking,
    SUM(CASE WHEN so.Status = 'Picked'          THEN 1 ELSE 0 END)  AS Picked,
    SUM(CASE WHEN so.Status = 'PartiallyPicked' THEN 1 ELSE 0 END)  AS PartiallyPicked,
    SUM(CASE WHEN so.Status = 'Cancelled'       THEN 1 ELSE 0 END)  AS Cancelled
FROM outbound.SalesOrders so
JOIN master.Customers  c  ON c.Id  = so.CustomerId
JOIN master.Warehouses wh ON wh.Id = so.WarehouseId
WHERE (@CustomerCode  IS NULL OR c.Code     = @CustomerCode)
  AND (@WarehouseCode IS NULL OR wh.Code    = @WarehouseCode)
  AND (@SearchLike    IS NULL OR so.SoNumber LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<SalesOrderStatusCounts>(
            new CommandDefinition(
                sql,
                new { f.CustomerCode, f.WarehouseCode, SearchLike = searchLike },
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SalesOrderLineRow>> GetLineRowsByIdAsync(
        Guid salesOrderId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    sol.Id,
    sol.LineNumber,
    sol.ProductId,
    p.Code  AS ProductCode,
    p.Name  AS ProductName,
    ow.Code AS OwnerCode,
    u.Code  AS UomCode,
    sol.OrderedQuantity,
    sol.AllocatedQuantity,
    sol.PickedQuantity,
    sol.UnitPrice,
    sol.Notes
FROM outbound.SalesOrderLines sol
JOIN master.Products       p  ON p.Id  = sol.ProductId
JOIN master.Owners         ow ON ow.Id = sol.OwnerId
JOIN master.UnitsOfMeasure u  ON u.Id  = sol.UomId
WHERE sol.SalesOrderId = @soId
ORDER BY sol.LineNumber;";

        var rows = await _connection.QueryAsync<SalesOrderLineRow>(new CommandDefinition(
            sql, new { soId = salesOrderId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> UpdateHeaderAsync(
        SalesOrder entity, Guid? userId, CancellationToken ct = default)
    {
        // SoNumber + CustomerId + WarehouseId omitted from SET — natural-
        // key shape, frozen post-create. OrderDate + RequestedShipDate +
        // Notes editable. Status flips go through SetStatusAsync.
        const string sql = @"
UPDATE outbound.SalesOrders SET
    OrderDate         = @OrderDate,
    RequestedShipDate = @RequestedShipDate,
    Notes             = @Notes,
    UpdatedAt         = SYSUTCDATETIME(),
    UpdatedBy         = @UserId,
    Version           = Version + 1
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.OrderDate,
                entity.RequestedShipDate,
                entity.Notes,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task ReplaceLinesAsync(
        Guid salesOrderId,
        IReadOnlyList<SalesOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM outbound.SalesOrderLines WHERE SalesOrderId = @soId;",
                new { soId = salesOrderId },
                transaction: tx,
                cancellationToken: ct));

            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO outbound.SalesOrderLines
                          (Id, SalesOrderId, LineNumber,
                           ProductId, OwnerId, UomId,
                           OrderedQuantity, UnitPrice, Notes, CreatedBy)
                      VALUES
                          (@Id, @SalesOrderId, @LineNumber,
                           @ProductId, @OwnerId, @UomId,
                           @OrderedQuantity, @UnitPrice, @Notes, @UserId);",
                    new
                    {
                        line.Id,
                        line.SalesOrderId,
                        line.LineNumber,
                        line.ProductId,
                        line.OwnerId,
                        line.UomId,
                        line.OrderedQuantity,
                        line.UnitPrice,
                        line.Notes,
                        UserId = userId,
                    },
                    transaction: tx,
                    cancellationToken: ct));
            }

            tx?.Commit();
        }
        catch
        {
            tx?.Rollback();
            throw;
        }
    }

    public async Task<bool> SetStatusAsync(
        Guid salesOrderId,
        string fromStatus,
        string toStatus,
        Guid? userId,
        CancellationToken ct = default)
    {
        const string sql = @"
UPDATE outbound.SalesOrders
SET Status    = @ToStatus,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @UserId,
    Version   = Version + 1
WHERE Id = @Id AND Status = @FromStatus;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = salesOrderId,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM outbound.SalesOrders
              WHERE SoNumber LIKE @prefix + '%';",
            new { prefix = datePrefix },
            cancellationToken: ct));

    public Task AdjustLineAllocatedQuantityAsync(
        Guid salesOrderLineId,
        decimal delta,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE outbound.SalesOrderLines
              SET AllocatedQuantity = AllocatedQuantity + @Delta,
                  UpdatedAt         = SYSUTCDATETIME(),
                  UpdatedBy         = @UserId,
                  Version           = Version + 1
              WHERE Id = @LineId;",
            new { LineId = salesOrderLineId, Delta = delta, UserId = userId },
            cancellationToken: ct));

    public Task AdjustLinePickedQuantityAsync(
        Guid salesOrderLineId,
        decimal delta,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE outbound.SalesOrderLines
              SET PickedQuantity = PickedQuantity + @Delta,
                  UpdatedAt      = SYSUTCDATETIME(),
                  UpdatedBy      = @UserId,
                  Version        = Version + 1
              WHERE Id = @LineId;",
            new { LineId = salesOrderLineId, Delta = delta, UserId = userId },
            cancellationToken: ct));
}
