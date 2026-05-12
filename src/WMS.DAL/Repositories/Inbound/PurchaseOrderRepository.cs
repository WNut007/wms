using System.Data;
using System.Transactions;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.DAL.Common;
using WMS.Domain.Entities.Inbound;
using IsolationLevel = System.Data.IsolationLevel;

namespace WMS.DAL.Repositories.Inbound;

// Dapper-backed PO repo. CreateAsync owns its transaction (open the
// connection here, BEGIN / COMMIT / ROLLBACK around the multi-row
// inserts) so the service stays free of ADO.NET plumbing. Reads use
// QueryMultiple to fetch header + lines in one round-trip.
internal sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private const string HeaderColumns = @"
        Id, PoNumber, OwnerId, WarehouseId, ExpectedDate,
        Status, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inbound.PurchaseOrders";

    private const string LineColumns = @"
        Id, PurchaseOrderId, LineNumber, ProductId, UomId,
        ExpectedQuantity, ReceivedQuantity, Status,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inbound.PurchaseOrderLines";

    private readonly IDbConnection _connection;

    public PurchaseOrderRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        PurchaseOrder header,
        IReadOnlyList<PurchaseOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default)
    {
        // Connection might be closed by the factory; open it before
        // beginning a transaction.
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        // TD-022 — defer to ambient TransactionScope when one exists
        // (orchestrator owns the transaction); else maintain the
        // standalone local-TX behaviour.
        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO inbound.PurchaseOrders
                      (Id, PoNumber, OwnerId, WarehouseId, ExpectedDate,
                       Status, Notes, CreatedBy)
                  VALUES
                      (@Id, @PoNumber, @OwnerId, @WarehouseId, @ExpectedDate,
                       @Status, @Notes, @UserId);",
                new
                {
                    header.Id,
                    header.PoNumber,
                    header.OwnerId,
                    header.WarehouseId,
                    header.ExpectedDate,
                    header.Status,
                    header.Notes,
                    UserId = userId,
                },
                transaction: tx,
                cancellationToken: ct));

            // Dapper expands an IEnumerable parameter into a multi-row
            // INSERT under the hood, but multi-row INSERT doesn't accept
            // a list-of-records cleanly with named columns — issue one
            // INSERT per line. Line counts are operator-scale (typically
            // <50), so the round-trips don't dominate.
            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO inbound.PurchaseOrderLines
                          (Id, PurchaseOrderId, LineNumber, ProductId, UomId,
                           ExpectedQuantity, ReceivedQuantity, Status, CreatedBy)
                      VALUES
                          (@Id, @PurchaseOrderId, @LineNumber, @ProductId, @UomId,
                           @ExpectedQuantity, @ReceivedQuantity, @Status, @UserId);",
                    new
                    {
                        line.Id,
                        line.PurchaseOrderId,
                        line.LineNumber,
                        line.ProductId,
                        line.UomId,
                        line.ExpectedQuantity,
                        line.ReceivedQuantity,
                        line.Status,
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

    public Task<PurchaseOrderDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE PurchaseOrderId = @id ORDER BY LineNumber;",
            new { id },
            ct);

    public Task<PurchaseOrderDetail?> GetByNumberAsync(string poNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            // Resolve the header by PoNumber, then match lines by the
            // header's Id — one round-trip via the QueryMultiple batch.
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM inbound.PurchaseOrders WHERE PoNumber = @poNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE PurchaseOrderId = @id ORDER BY LineNumber;",
            new { poNumber },
            ct);

    private async Task<PurchaseOrderDetail?> ReadDetailAsync(
        string sql,
        object args,
        CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<PurchaseOrder?>();
        if (header is null) return null;

        var lines = (await multi.ReadAsync<PurchaseOrderLine>()).AsList();
        return new PurchaseOrderDetail(header, lines);
    }

    public Task IncrementLineReceivedQuantityAsync(
        Guid poLineId,
        decimal delta,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE inbound.PurchaseOrderLines
              SET ReceivedQuantity = ReceivedQuantity + @Delta,
                  UpdatedAt        = SYSUTCDATETIME(),
                  UpdatedBy        = @UserId,
                  Version          = Version + 1
              WHERE Id = @PoLineId;",
            new
            {
                PoLineId = poLineId,
                Delta = delta,
                UserId = userId,
            },
            cancellationToken: ct));

    // ================================================================
    // Phase 9A list + edit + status surface
    // ================================================================

    public async Task<PagedResult<PurchaseOrderListRow>> GetPagedAsync(
        PurchaseOrderFilter f, CancellationToken ct = default)
    {
        var orderBy   = PurchaseOrderSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip      = (f.Page - 1) * f.PageSize;
        var take      = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        // CTE materialises the per-PO line aggregate once. ISNULL
        // protects against POs with zero lines (shouldn't exist post-
        // 9A's "lines required" rule, but defensive).
        const string whereClause = @"
WHERE (@Status      IS NULL OR po.Status = @Status)
  AND (@OwnerCode   IS NULL OR ow.Code   = @OwnerCode)
  AND (@WarehouseCode IS NULL OR wh.Code = @WarehouseCode)
  AND (@SearchLike  IS NULL OR po.PoNumber LIKE @SearchLike)";

        var sql = $@"
WITH agg AS (
    SELECT PurchaseOrderId,
           COUNT(*) AS LineCount,
           SUM(ExpectedQuantity) AS TotalExpectedQty,
           SUM(ReceivedQuantity) AS TotalReceivedQty
    FROM inbound.PurchaseOrderLines
    GROUP BY PurchaseOrderId
)
SELECT
    po.Id, po.PoNumber,
    po.OwnerId, ow.Code AS OwnerCode, ow.Name AS OwnerName,
    po.WarehouseId, wh.Code AS WarehouseCode,
    po.ExpectedDate, po.Status,
    ISNULL(agg.LineCount,        0) AS LineCount,
    ISNULL(agg.TotalExpectedQty, 0) AS TotalExpectedQty,
    ISNULL(agg.TotalReceivedQty, 0) AS TotalReceivedQty,
    po.CreatedAt, po.UpdatedAt
FROM inbound.PurchaseOrders po
JOIN master.Owners     ow ON ow.Id = po.OwnerId
JOIN master.Warehouses wh ON wh.Id = po.WarehouseId
LEFT JOIN agg ON agg.PurchaseOrderId = po.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM inbound.PurchaseOrders po
JOIN master.Owners     ow ON ow.Id = po.OwnerId
JOIN master.Warehouses wh ON wh.Id = po.WarehouseId
{whereClause};";

        var args = new
        {
            f.Status,
            f.OwnerCode,
            f.WarehouseCode,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<PurchaseOrderListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<PurchaseOrderListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<bool> UpdateHeaderAsync(
        PurchaseOrder entity, Guid? userId, CancellationToken ct = default)
    {
        // PoNumber + OwnerId + WarehouseId omitted — natural-key shape;
        // changing them would orphan receipt FKs. ExpectedDate + Notes
        // are the editable fields. Status changes go through
        // SetStatusAsync (atomic, idempotent).
        const string sql = @"
UPDATE inbound.PurchaseOrders SET
    ExpectedDate = @ExpectedDate,
    Notes        = @Notes,
    UpdatedAt    = SYSUTCDATETIME(),
    UpdatedBy    = @UserId,
    Version      = Version + 1
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.ExpectedDate,
                entity.Notes,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task ReplaceLinesAsync(
        Guid purchaseOrderId,
        IReadOnlyList<PurchaseOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        // TD-022 — same ambient-aware pattern as CreateAsync.
        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inbound.PurchaseOrderLines WHERE PurchaseOrderId = @poId;",
                new { poId = purchaseOrderId },
                transaction: tx,
                cancellationToken: ct));

            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO inbound.PurchaseOrderLines
                          (Id, PurchaseOrderId, LineNumber, ProductId, UomId,
                           ExpectedQuantity, ReceivedQuantity, Status, CreatedBy)
                      VALUES
                          (@Id, @PurchaseOrderId, @LineNumber, @ProductId, @UomId,
                           @ExpectedQuantity, @ReceivedQuantity, @Status, @UserId);",
                    new
                    {
                        line.Id,
                        line.PurchaseOrderId,
                        line.LineNumber,
                        line.ProductId,
                        line.UomId,
                        line.ExpectedQuantity,
                        line.ReceivedQuantity,
                        line.Status,
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
        Guid purchaseOrderId,
        string fromStatus,
        string toStatus,
        Guid? userId,
        CancellationToken ct = default)
    {
        // Idempotent: WHERE Status=@from means "0 rows affected" if the
        // PO is already in the target state. Returning the bool lets the
        // service distinguish "actually changed" from "no-op".
        const string sql = @"
UPDATE inbound.PurchaseOrders
SET Status    = @ToStatus,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @UserId,
    Version   = Version + 1
WHERE Id = @Id AND Status = @FromStatus;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = purchaseOrderId,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public Task<int> CountReceivedLinesAsync(
        Guid purchaseOrderId, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*)
              FROM inbound.PurchaseOrderLines
              WHERE PurchaseOrderId = @poId AND ReceivedQuantity > 0;",
            new { poId = purchaseOrderId },
            cancellationToken: ct));

    public async Task<bool> AllLinesFullyReceivedAsync(
        Guid purchaseOrderId, CancellationToken ct = default)
    {
        // True when no line has ReceivedQty < ExpectedQty (= every line
        // is at-or-over the expected). Cancelled lines are excluded —
        // they don't count against the "all received" predicate.
        const string sql = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM inbound.PurchaseOrderLines
    WHERE PurchaseOrderId = @poId
      AND Status <> 'Cancelled'
      AND ReceivedQuantity < ExpectedQuantity
) THEN 0 ELSE 1 END;";

        var has = await _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { poId = purchaseOrderId }, cancellationToken: ct));
        return has == 1;
    }

    public Task CancelOpenLinesAsync(
        Guid purchaseOrderId, Guid? userId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE inbound.PurchaseOrderLines
              SET Status    = 'Cancelled',
                  UpdatedAt = SYSUTCDATETIME(),
                  UpdatedBy = @UserId,
                  Version   = Version + 1
              WHERE PurchaseOrderId = @poId
                AND Status IN ('Open', 'PartiallyReceived');",
            new { poId = purchaseOrderId, UserId = userId },
            cancellationToken: ct));

    // ================================================================
    // Phase 10A — PO Detail Lines tab + chip-count aggregates
    // ================================================================

    public async Task<IReadOnlyList<PurchaseOrderLineRow>> GetLineRowsByIdAsync(
        Guid purchaseOrderId, CancellationToken ct = default)
    {
        // INNER JOINs — Product + UoM are required FKs on the line; no
        // null-row risk. Sorted by LineNumber to match the on-paper view.
        const string sql = @"
SELECT
    pol.Id,
    pol.LineNumber,
    pol.ProductId,
    p.Code        AS ProductCode,
    p.Name        AS ProductName,
    pol.UomId,
    u.Code        AS UomCode,
    pol.ExpectedQuantity,
    pol.ReceivedQuantity,
    pol.Status
FROM inbound.PurchaseOrderLines pol
JOIN master.Products        p ON p.Id = pol.ProductId
JOIN master.UnitsOfMeasure  u ON u.Id = pol.UomId
WHERE pol.PurchaseOrderId = @poId
ORDER BY pol.LineNumber;";

        var rows = await _connection.QueryAsync<PurchaseOrderLineRow>(new CommandDefinition(
            sql, new { poId = purchaseOrderId }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<bool> AnyLineHasReceiptsAsync(
        Guid purchaseOrderId, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            @"SELECT CASE WHEN EXISTS (
                  SELECT 1 FROM inbound.PurchaseOrderLines
                  WHERE PurchaseOrderId = @poId
                    AND Status <> 'Cancelled'
                    AND ReceivedQuantity > 0
              ) THEN 1 ELSE 0 END;",
            new { poId = purchaseOrderId },
            cancellationToken: ct));

    public Task RevertLineStatusAsync(
        Guid purchaseOrderLineId, Guid? userId, CancellationToken ct = default) =>
        // Server-side derivation — UPDATE only when the computed
        // target differs from current Status, to keep Version stable
        // when nothing changed (e.g. the line was already Open).
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE inbound.PurchaseOrderLines
              SET Status    = CASE
                                WHEN ReceivedQuantity = 0                              THEN 'Open'
                                WHEN ReceivedQuantity < ExpectedQuantity               THEN 'PartiallyReceived'
                                ELSE                                                        'Closed'
                              END,
                  UpdatedAt = SYSUTCDATETIME(),
                  UpdatedBy = @UserId,
                  Version   = Version + 1
              WHERE Id     = @LineId
                AND Status <> 'Cancelled'
                AND Status <> CASE
                                WHEN ReceivedQuantity = 0                              THEN 'Open'
                                WHEN ReceivedQuantity < ExpectedQuantity               THEN 'PartiallyReceived'
                                ELSE                                                        'Closed'
                              END;",
            new { LineId = purchaseOrderLineId, UserId = userId },
            cancellationToken: ct));

    public async Task<PurchaseOrderStatusCounts> GetStatusCountsAsync(
        PurchaseOrderFilter f, CancellationToken ct = default)
    {
        // Single SUM(CASE WHEN) round-trip. Same WHERE shape as
        // GetPagedAsync EXCEPT @Status is intentionally absent — counts
        // are per-status; the chips need totals across all statuses to
        // render even when the user has narrowed via a status chip.
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                           AS [All],
    SUM(CASE WHEN po.Status = 'Open'      THEN 1 ELSE 0 END)           AS [Open],
    SUM(CASE WHEN po.Status = 'Receiving' THEN 1 ELSE 0 END)           AS Receiving,
    SUM(CASE WHEN po.Status = 'Closed'    THEN 1 ELSE 0 END)           AS Closed,
    SUM(CASE WHEN po.Status = 'Cancelled' THEN 1 ELSE 0 END)           AS Cancelled
FROM inbound.PurchaseOrders po
JOIN master.Owners     ow ON ow.Id = po.OwnerId
JOIN master.Warehouses wh ON wh.Id = po.WarehouseId
WHERE (@OwnerCode     IS NULL OR ow.Code     = @OwnerCode)
  AND (@WarehouseCode IS NULL OR wh.Code     = @WarehouseCode)
  AND (@SearchLike    IS NULL OR po.PoNumber LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<PurchaseOrderStatusCounts>(
            new CommandDefinition(
                sql,
                new { f.OwnerCode, f.WarehouseCode, SearchLike = searchLike },
                cancellationToken: ct));
    }
}
