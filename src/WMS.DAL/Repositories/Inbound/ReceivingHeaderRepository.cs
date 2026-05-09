using System.Data;
using System.Transactions;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.DAL.Common;
using WMS.Domain.Entities.Inbound;
using IsolationLevel = System.Data.IsolationLevel;

namespace WMS.DAL.Repositories.Inbound;

// Same shape as PurchaseOrderRepository — Create owns its transaction;
// reads use Dapper QueryMultiple to fetch header + lines in one
// round-trip.
internal sealed class ReceivingHeaderRepository : IReceivingHeaderRepository
{
    private const string HeaderColumns = @"
        Id, ReceivingNumber, PurchaseOrderId, WarehouseId, ReceivedAt,
        Status, Notes,
        CancelledBy, CancelledAt, CancelReason,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inbound.ReceivingHeaders";

    private const string LineColumns = @"
        Id, ReceivingHeaderId, LineNumber, PurchaseOrderLineId,
        ProductId, UomId, OwnerId, LocationId,
        ReceivedQuantity, LotNumber, PalletNumber, LotId, PalletId,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inbound.ReceivingLines";

    private readonly IDbConnection _connection;

    public ReceivingHeaderRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        ReceivingHeader header,
        IReadOnlyList<ReceivingLine> lines,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        // TD-022 — when the orchestrator already wraps us in a
        // TransactionScope, the connection is enlisted in the ambient
        // transaction and SqlConnection.BeginTransaction() throws
        // ("SqlConnection does not support parallel transactions").
        // Defer to the ambient TX in that case (Dapper auto-uses it
        // when no explicit `transaction:` is passed); else keep the
        // local-TX behaviour for callers running us standalone.
        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO inbound.ReceivingHeaders
                      (Id, ReceivingNumber, PurchaseOrderId, WarehouseId,
                       ReceivedAt, Status, Notes, CreatedBy)
                  VALUES
                      (@Id, @ReceivingNumber, @PurchaseOrderId, @WarehouseId,
                       @ReceivedAt, @Status, @Notes, @UserId);",
                new
                {
                    header.Id,
                    header.ReceivingNumber,
                    header.PurchaseOrderId,
                    header.WarehouseId,
                    header.ReceivedAt,
                    header.Status,
                    header.Notes,
                    UserId = userId,
                },
                transaction: tx,
                cancellationToken: ct));

            // Per-line INSERT — same rationale as PurchaseOrderRepository
            // (Dapper's enumerable expansion fights named-column inserts;
            // line counts are operator-scale).
            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO inbound.ReceivingLines
                          (Id, ReceivingHeaderId, LineNumber, PurchaseOrderLineId,
                           ProductId, UomId, OwnerId, LocationId,
                           ReceivedQuantity, LotNumber, PalletNumber,
                           LotId, PalletId, CreatedBy)
                      VALUES
                          (@Id, @ReceivingHeaderId, @LineNumber, @PurchaseOrderLineId,
                           @ProductId, @UomId, @OwnerId, @LocationId,
                           @ReceivedQuantity, @LotNumber, @PalletNumber,
                           @LotId, @PalletId, @UserId);",
                    new
                    {
                        line.Id,
                        line.ReceivingHeaderId,
                        line.LineNumber,
                        line.PurchaseOrderLineId,
                        line.ProductId,
                        line.UomId,
                        line.OwnerId,
                        line.LocationId,
                        line.ReceivedQuantity,
                        line.LotNumber,
                        line.PalletNumber,
                        line.LotId,
                        line.PalletId,
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

    public Task<ReceivingDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE ReceivingHeaderId = @id ORDER BY LineNumber;",
            new { id },
            ct);

    public Task<ReceivingDetail?> GetByNumberAsync(string receivingNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM inbound.ReceivingHeaders WHERE ReceivingNumber = @receivingNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE ReceivingHeaderId = @id ORDER BY LineNumber;",
            new { receivingNumber },
            ct);

    public Task UpdateLineInventoryRefsAsync(
        Guid lineId,
        Guid? lotId,
        Guid? palletId,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE inbound.ReceivingLines
              SET LotId      = @LotId,
                  PalletId   = @PalletId,
                  UpdatedAt  = SYSUTCDATETIME(),
                  UpdatedBy  = @UserId,
                  Version    = Version + 1
              WHERE Id = @LineId;",
            new
            {
                LineId = lineId,
                LotId = lotId,
                PalletId = palletId,
                UserId = userId,
            },
            cancellationToken: ct));

    private async Task<ReceivingDetail?> ReadDetailAsync(
        string sql,
        object args,
        CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<ReceivingHeader?>();
        if (header is null) return null;

        var lines = (await multi.ReadAsync<ReceivingLine>()).AsList();
        return new ReceivingDetail(header, lines);
    }

    public async Task<PagedResult<ReceivingListRow>> GetPagedAsync(
        ReceivingFilter f, CancellationToken ct = default)
    {
        var orderBy   = ReceivingSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip      = (f.Page - 1) * f.PageSize;
        var take      = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        // CTE materialises the per-header line aggregate once. LEFT
        // JOINs to PurchaseOrders + Owners (both nullable for blind
        // receipts).
        const string whereClause = @"
WHERE (@Status IS NULL OR h.Status = @Status)
  AND (@WarehouseCode IS NULL OR wh.Code = @WarehouseCode)
  AND (@SearchLike IS NULL
       OR h.ReceivingNumber LIKE @SearchLike
       OR po.PoNumber LIKE @SearchLike)";

        var sql = $@"
WITH agg AS (
    SELECT ReceivingHeaderId,
           COUNT(*) AS LineCount,
           SUM(ReceivedQuantity) AS TotalReceivedQty
    FROM inbound.ReceivingLines
    GROUP BY ReceivingHeaderId
)
SELECT
    h.Id, h.ReceivingNumber,
    h.PurchaseOrderId,
    po.PoNumber AS PoNumber,
    ow.Code AS VendorCode,
    ow.Name AS VendorName,
    h.WarehouseId, wh.Code AS WarehouseCode,
    h.ReceivedAt, h.Status,
    ISNULL(agg.LineCount,        0) AS LineCount,
    ISNULL(agg.TotalReceivedQty, 0) AS TotalReceivedQty,
    h.CreatedAt
FROM inbound.ReceivingHeaders h
LEFT JOIN inbound.PurchaseOrders po ON po.Id = h.PurchaseOrderId
LEFT JOIN master.Owners          ow ON ow.Id = po.OwnerId
JOIN      master.Warehouses      wh ON wh.Id = h.WarehouseId
LEFT JOIN agg                       ON agg.ReceivingHeaderId = h.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM inbound.ReceivingHeaders h
LEFT JOIN inbound.PurchaseOrders po ON po.Id = h.PurchaseOrderId
JOIN      master.Warehouses      wh ON wh.Id = h.WarehouseId
{whereClause};";

        var args = new
        {
            f.Status,
            f.WarehouseCode,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<ReceivingListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<ReceivingListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<IReadOnlyList<ReceivingActivityRow>> GetActivityByPoAsync(
        Guid purchaseOrderId, int limit = 20, CancellationToken ct = default)
    {
        // IX_ReceivingHeaders_PurchaseOrder covers the WHERE; no
        // dedicated index on (PoId, ReceivedAt) but at typical volumes
        // (a PO has <20 receipts in its lifecycle) the sort is cheap.
        var rows = await _connection.QueryAsync<ReceivingActivityRow>(new CommandDefinition(
            @"SELECT
                  h.Id,
                  h.ReceivingNumber,
                  h.ReceivedAt,
                  h.Status,
                  COALESCE(u.FullName, u.Email, 'System') AS PerformedByName,
                  (SELECT COUNT(*) FROM inbound.ReceivingLines rl
                   WHERE rl.ReceivingHeaderId = h.Id) AS LineCount
              FROM inbound.ReceivingHeaders h
              LEFT JOIN security.Users u ON u.Id = h.CreatedBy
              WHERE h.PurchaseOrderId = @purchaseOrderId
              ORDER BY h.ReceivedAt DESC
              OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            new { purchaseOrderId, limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<PoReceiptRow>> GetReceiptsByPoIdAsync(
        Guid purchaseOrderId, CancellationToken ct = default)
    {
        // Mirrors GetActivityByPoAsync's shape but adds the line-sum
        // aggregate via a CTE so the table column renders without per-
        // row queries. IX_ReceivingHeaders_PurchaseOrder covers WHERE.
        const string sql = @"
WITH agg AS (
    SELECT ReceivingHeaderId,
           COUNT(*) AS LineCount,
           SUM(ReceivedQuantity) AS TotalReceivedQty
    FROM inbound.ReceivingLines
    GROUP BY ReceivingHeaderId
)
SELECT
    h.Id,
    h.ReceivingNumber,
    h.ReceivedAt,
    h.Status,
    ISNULL(agg.LineCount,        0) AS LineCount,
    ISNULL(agg.TotalReceivedQty, 0) AS TotalReceivedQty
FROM inbound.ReceivingHeaders h
LEFT JOIN agg ON agg.ReceivingHeaderId = h.Id
WHERE h.PurchaseOrderId = @purchaseOrderId
ORDER BY h.ReceivedAt DESC;";

        var rows = await _connection.QueryAsync<PoReceiptRow>(new CommandDefinition(
            sql, new { purchaseOrderId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> SetCancellationAsync(
        Guid receivingHeaderId,
        string reason,
        Guid? userId,
        CancellationToken ct = default)
    {
        // Idempotent: only Posted → Cancelled is a valid transition.
        // Already-Cancelled returns 0 rows affected; missing row also
        // returns 0; both surface as `false` to the caller.
        const string sql = @"
UPDATE inbound.ReceivingHeaders
SET Status       = 'Cancelled',
    CancelledBy  = @UserId,
    CancelledAt  = SYSUTCDATETIME(),
    CancelReason = @Reason,
    UpdatedAt    = SYSUTCDATETIME(),
    UpdatedBy    = @UserId,
    Version      = Version + 1
WHERE Id = @Id AND Status = 'Posted';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = receivingHeaderId, Reason = reason, UserId = userId },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<ReceivingStatusCounts> GetStatusCountsAsync(
        ReceivingFilter f, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        // LEFT JOINs on PurchaseOrders + Owners (blind receipts have
        // null PO); JOIN on Warehouses (always set). Search matches
        // ReceivingNumber OR PoNumber — same shape as GetPagedAsync.
        const string sql = @"
SELECT
    COUNT(*)                                                    AS [All],
    SUM(CASE WHEN h.Status = 'Draft'     THEN 1 ELSE 0 END)     AS Draft,
    SUM(CASE WHEN h.Status = 'Posted'    THEN 1 ELSE 0 END)     AS Posted,
    SUM(CASE WHEN h.Status = 'Cancelled' THEN 1 ELSE 0 END)     AS Cancelled
FROM inbound.ReceivingHeaders h
LEFT JOIN inbound.PurchaseOrders po ON po.Id = h.PurchaseOrderId
JOIN      master.Warehouses     wh ON wh.Id = h.WarehouseId
WHERE (@WarehouseCode IS NULL OR wh.Code = @WarehouseCode)
  AND (@SearchLike    IS NULL
       OR h.ReceivingNumber LIKE @SearchLike
       OR po.PoNumber       LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<ReceivingStatusCounts>(
            new CommandDefinition(
                sql,
                new { f.WarehouseCode, SearchLike = searchLike },
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ReceivingActivityRow>> GetActivityByWarehouseAsync(
        Guid warehouseId, int limit = 20, CancellationToken ct = default)
    {
        // IX_ReceivingHeaders_Warehouse(WarehouseId, ReceivedAt DESC)
        // covers the WHERE + ORDER exactly. The line-count subquery is
        // a correlated SELECT COUNT(*) — at <=20 rows that's a row-by-
        // row index lookup against ReceivingLines.ReceivingHeaderId,
        // which is the line table's leading FK index. Cheap.
        // COALESCE handles NULL PerformedBy (system-imported headers)
        // → falls back to literal 'System', same convention as
        // StockMovementRepository.GetByProductAsync.
        var rows = await _connection.QueryAsync<ReceivingActivityRow>(new CommandDefinition(
            @"SELECT
                  h.Id,
                  h.ReceivingNumber,
                  h.ReceivedAt,
                  h.Status,
                  COALESCE(u.FullName, u.Email, 'System') AS PerformedByName,
                  (SELECT COUNT(*) FROM inbound.ReceivingLines rl
                   WHERE rl.ReceivingHeaderId = h.Id) AS LineCount
              FROM inbound.ReceivingHeaders h
              LEFT JOIN security.Users u ON u.Id = h.CreatedBy
              WHERE h.WarehouseId = @warehouseId
              ORDER BY h.ReceivedAt DESC
              OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            new { warehouseId, limit },
            cancellationToken: ct));
        return rows.AsList();
    }
}
