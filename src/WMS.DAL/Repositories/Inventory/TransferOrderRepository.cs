using System.Data;
using System.Transactions;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.DAL.Common;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

internal sealed class TransferOrderRepository : ITransferOrderRepository
{
    private const string HeaderColumns = @"
        Id, TransferNumber, FromWarehouseId, ToWarehouseId,
        Status, Reason, Notes,
        RequestedBy, RequestedAt,
        SubmittedBy, SubmittedAt,
        ApprovedBy, ApprovedAt,
        DispatchedBy, DispatchedAt,
        ReceivedBy, ReceivedAt,
        CancelledBy, CancelledAt, CancelReason,
        LostBy, LostAt, LossReason,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inventory.TransferOrders";

    private const string LineColumns = @"
        Id, TransferId, LineNumber,
        ProductId, OwnerId, UomId, LotId, PalletId,
        FromLocationId, ToLocationId,
        QtyRequested, QtyDispatched, QtyReceived, QtyLossInTransit,
        Status, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM inventory.TransferOrderLines";

    private readonly IDbConnection _connection;

    public TransferOrderRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        TransferOrder header,
        IReadOnlyList<TransferOrderLine> lines,
        Guid requestedBy,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        // TD-022 ambient-detection pattern (Phase 10B precedent).
        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO inventory.TransferOrders
                      (Id, TransferNumber, FromWarehouseId, ToWarehouseId,
                       Status, Reason, Notes, RequestedBy, CreatedBy)
                  VALUES
                      (@Id, @TransferNumber, @FromWarehouseId, @ToWarehouseId,
                       @Status, @Reason, @Notes, @RequestedBy, @RequestedBy);",
                new
                {
                    header.Id,
                    header.TransferNumber,
                    header.FromWarehouseId,
                    header.ToWarehouseId,
                    header.Status,
                    header.Reason,
                    header.Notes,
                    RequestedBy = requestedBy,
                },
                transaction: tx,
                cancellationToken: ct));

            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO inventory.TransferOrderLines
                          (Id, TransferId, LineNumber,
                           ProductId, OwnerId, UomId, LotId, PalletId,
                           FromLocationId, ToLocationId,
                           QtyRequested, Status, Notes, CreatedBy)
                      VALUES
                          (@Id, @TransferId, @LineNumber,
                           @ProductId, @OwnerId, @UomId, @LotId, @PalletId,
                           @FromLocationId, @ToLocationId,
                           @QtyRequested, @Status, @Notes, @RequestedBy);",
                    new
                    {
                        line.Id,
                        line.TransferId,
                        line.LineNumber,
                        line.ProductId,
                        line.OwnerId,
                        line.UomId,
                        line.LotId,
                        line.PalletId,
                        line.FromLocationId,
                        line.ToLocationId,
                        line.QtyRequested,
                        line.Status,
                        line.Notes,
                        RequestedBy = requestedBy,
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

    public Task<TransferOrderDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE TransferId = @id ORDER BY LineNumber;",
            new { id }, ct);

    public Task<TransferOrderDetail?> GetByNumberAsync(string transferNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM inventory.TransferOrders WHERE TransferNumber = @transferNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE TransferId = @id ORDER BY LineNumber;",
            new { transferNumber }, ct);

    private async Task<TransferOrderDetail?> ReadDetailAsync(
        string sql, object args, CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));
        var header = await multi.ReadSingleOrDefaultAsync<TransferOrder?>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<TransferOrderLine>()).AsList();
        return new TransferOrderDetail(header, lines);
    }

    public async Task<IReadOnlyList<TransferOrderLineRow>> GetLineRowsByIdAsync(
        Guid transferId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    tl.Id,
    tl.LineNumber,
    tl.ProductId,
    p.Code            AS ProductCode,
    p.Name            AS ProductName,
    tl.FromLocationId,
    floc.Code         AS FromLocationCode,
    tl.ToLocationId,
    tloc.Code         AS ToLocationCode,
    u.Code            AS UomCode,
    ow.Code           AS OwnerCode,
    lot.LotNumber     AS LotNumber,
    pal.PalletNumber  AS PalletNumber,
    tl.QtyRequested,
    tl.QtyDispatched,
    tl.QtyReceived,
    tl.QtyLossInTransit,
    tl.Status,
    tl.Notes
FROM inventory.TransferOrderLines tl
JOIN master.Products       p    ON p.Id    = tl.ProductId
JOIN master.Locations      floc ON floc.Id = tl.FromLocationId
JOIN master.Locations      tloc ON tloc.Id = tl.ToLocationId
JOIN master.UnitsOfMeasure u    ON u.Id    = tl.UomId
JOIN master.Owners         ow   ON ow.Id   = tl.OwnerId
LEFT JOIN inventory.Lots    lot ON lot.Id = tl.LotId
LEFT JOIN inventory.Pallets pal ON pal.Id = tl.PalletId
WHERE tl.TransferId = @transferId
ORDER BY tl.LineNumber;";

        var rows = await _connection.QueryAsync<TransferOrderLineRow>(new CommandDefinition(
            sql, new { transferId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PagedResult<TransferOrderListRow>> GetPagedAsync(
        TransferOrderFilter f, CancellationToken ct = default)
    {
        var orderBy = TransferOrderSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip = (f.Page - 1) * f.PageSize;
        var take = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@Status            IS NULL OR t.Status = @Status)
  AND (@FromWarehouseCode IS NULL OR fwh.Code = @FromWarehouseCode)
  AND (@ToWarehouseCode   IS NULL OR twh.Code = @ToWarehouseCode)
  AND (@SearchLike        IS NULL OR t.TransferNumber LIKE @SearchLike)";

        var sql = $@"
WITH agg AS (
    SELECT TransferId,
           COUNT(*) AS LineCount,
           SUM(QtyRequested)         AS TotalRequested,
           SUM(ISNULL(QtyDispatched, 0)) AS TotalDispatched,
           SUM(ISNULL(QtyReceived,   0)) AS TotalReceived
    FROM inventory.TransferOrderLines
    GROUP BY TransferId
)
SELECT
    t.Id, t.TransferNumber,
    t.FromWarehouseId, fwh.Code AS FromWarehouseCode,
    t.ToWarehouseId,   twh.Code AS ToWarehouseCode,
    t.Status, t.Reason,
    ISNULL(agg.LineCount,        0) AS LineCount,
    ISNULL(agg.TotalRequested,   0) AS TotalRequested,
    ISNULL(agg.TotalDispatched,  0) AS TotalDispatched,
    ISNULL(agg.TotalReceived,    0) AS TotalReceived,
    COALESCE(u.FullName, u.Email, 'System') AS RequestedByName,
    t.RequestedAt
FROM inventory.TransferOrders t
JOIN master.Warehouses fwh ON fwh.Id = t.FromWarehouseId
JOIN master.Warehouses twh ON twh.Id = t.ToWarehouseId
LEFT JOIN security.Users u ON u.Id = t.RequestedBy
LEFT JOIN agg ON agg.TransferId = t.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM inventory.TransferOrders t
JOIN master.Warehouses fwh ON fwh.Id = t.FromWarehouseId
JOIN master.Warehouses twh ON twh.Id = t.ToWarehouseId
{whereClause};";

        var args = new
        {
            f.Status,
            f.FromWarehouseCode,
            f.ToWarehouseCode,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<TransferOrderListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<TransferOrderListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<TransferOrderStatusCounts> GetStatusCountsAsync(
        TransferOrderFilter f, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                  AS [All],
    SUM(CASE WHEN t.Status = 'Draft'      THEN 1 ELSE 0 END)  AS Draft,
    SUM(CASE WHEN t.Status = 'Submitted'  THEN 1 ELSE 0 END)  AS Submitted,
    SUM(CASE WHEN t.Status = 'Approved'   THEN 1 ELSE 0 END)  AS Approved,
    SUM(CASE WHEN t.Status = 'InTransit'  THEN 1 ELSE 0 END)  AS InTransit,
    SUM(CASE WHEN t.Status = 'Received'   THEN 1 ELSE 0 END)  AS Received,
    SUM(CASE WHEN t.Status = 'Cancelled'  THEN 1 ELSE 0 END)  AS Cancelled,
    SUM(CASE WHEN t.Status = 'Lost'       THEN 1 ELSE 0 END)  AS Lost
FROM inventory.TransferOrders t
JOIN master.Warehouses fwh ON fwh.Id = t.FromWarehouseId
JOIN master.Warehouses twh ON twh.Id = t.ToWarehouseId
WHERE (@FromWarehouseCode IS NULL OR fwh.Code = @FromWarehouseCode)
  AND (@ToWarehouseCode   IS NULL OR twh.Code = @ToWarehouseCode)
  AND (@SearchLike        IS NULL OR t.TransferNumber LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<TransferOrderStatusCounts>(
            new CommandDefinition(
                sql,
                new { f.FromWarehouseCode, f.ToWarehouseCode, SearchLike = searchLike },
                cancellationToken: ct));
    }

    // ================================================================
    // State transitions — atomic + idempotent via WHERE Status=@from
    // ================================================================

    public async Task<bool> SetSubmittedAsync(
        Guid transferId, Guid submittedBy, CancellationToken ct = default)
    {
        var rows = await _connection.ExecuteAsync(new CommandDefinition(@"
UPDATE inventory.TransferOrders
SET Status      = 'Submitted',
    SubmittedBy = @UserId,
    SubmittedAt = SYSUTCDATETIME(),
    UpdatedAt   = SYSUTCDATETIME(),
    UpdatedBy   = @UserId,
    Version     = Version + 1
WHERE Id = @Id AND Status = 'Draft';",
            new { Id = transferId, UserId = submittedBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetApprovedAsync(
        Guid transferId, Guid approvedBy, CancellationToken ct = default)
    {
        var rows = await _connection.ExecuteAsync(new CommandDefinition(@"
UPDATE inventory.TransferOrders
SET Status     = 'Approved',
    ApprovedBy = @UserId,
    ApprovedAt = SYSUTCDATETIME(),
    UpdatedAt  = SYSUTCDATETIME(),
    UpdatedBy  = @UserId,
    Version    = Version + 1
WHERE Id = @Id AND Status = 'Submitted';",
            new { Id = transferId, UserId = approvedBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetInTransitAsync(
        Guid transferId, Guid dispatchedBy, CancellationToken ct = default)
    {
        var rows = await _connection.ExecuteAsync(new CommandDefinition(@"
UPDATE inventory.TransferOrders
SET Status       = 'InTransit',
    DispatchedBy = @UserId,
    DispatchedAt = SYSUTCDATETIME(),
    UpdatedAt    = SYSUTCDATETIME(),
    UpdatedBy    = @UserId,
    Version      = Version + 1
WHERE Id = @Id AND Status = 'Approved';",
            new { Id = transferId, UserId = dispatchedBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetReceivedAsync(
        Guid transferId, Guid receivedBy, CancellationToken ct = default)
    {
        var rows = await _connection.ExecuteAsync(new CommandDefinition(@"
UPDATE inventory.TransferOrders
SET Status     = 'Received',
    ReceivedBy = @UserId,
    ReceivedAt = SYSUTCDATETIME(),
    UpdatedAt  = SYSUTCDATETIME(),
    UpdatedBy  = @UserId,
    Version    = Version + 1
WHERE Id = @Id AND Status = 'InTransit';",
            new { Id = transferId, UserId = receivedBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetCancelledAsync(
        Guid transferId, string fromStatus, string reason,
        Guid cancelledBy, CancellationToken ct = default)
    {
        var rows = await _connection.ExecuteAsync(new CommandDefinition(@"
UPDATE inventory.TransferOrders
SET Status       = 'Cancelled',
    CancelledBy  = @UserId,
    CancelledAt  = SYSUTCDATETIME(),
    CancelReason = @Reason,
    UpdatedAt    = SYSUTCDATETIME(),
    UpdatedBy    = @UserId,
    Version      = Version + 1
WHERE Id = @Id AND Status = @FromStatus;",
            new { Id = transferId, FromStatus = fromStatus, Reason = reason, UserId = cancelledBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetLostAsync(
        Guid transferId, string reason, Guid lostBy, CancellationToken ct = default)
    {
        var rows = await _connection.ExecuteAsync(new CommandDefinition(@"
UPDATE inventory.TransferOrders
SET Status     = 'Lost',
    LostBy     = @UserId,
    LostAt     = SYSUTCDATETIME(),
    LossReason = @Reason,
    UpdatedAt  = SYSUTCDATETIME(),
    UpdatedBy  = @UserId,
    Version    = Version + 1
WHERE Id = @Id AND Status = 'InTransit';",
            new { Id = transferId, Reason = reason, UserId = lostBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public Task UpdateLineDispatchedAsync(
        Guid lineId, decimal qtyDispatched, Guid currentUserId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(@"
UPDATE inventory.TransferOrderLines
SET QtyDispatched = @QtyDispatched,
    Status        = 'Dispatched',
    UpdatedAt     = SYSUTCDATETIME(),
    UpdatedBy     = @UserId
WHERE Id = @LineId AND Status = 'Pending';",
            new { LineId = lineId, QtyDispatched = qtyDispatched, UserId = currentUserId },
            cancellationToken: ct));

    public Task UpdateLineReceivedAsync(
        Guid lineId, decimal qtyReceived, string targetStatus,
        Guid currentUserId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(@"
UPDATE inventory.TransferOrderLines
SET QtyReceived = @QtyReceived,
    Status      = @TargetStatus,
    UpdatedAt   = SYSUTCDATETIME(),
    UpdatedBy   = @UserId
WHERE Id = @LineId AND Status = 'Dispatched';",
            new { LineId = lineId, QtyReceived = qtyReceived,
                  TargetStatus = targetStatus, UserId = currentUserId },
            cancellationToken: ct));

    public Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM inventory.TransferOrders
              WHERE TransferNumber LIKE @prefix + '%';",
            new { prefix = datePrefix },
            cancellationToken: ct));
}
