using System.Data;
using System.Transactions;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.DAL.Common;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14D — Dapper-backed PackTask repo. CreateAsync owns a TX
// (ambient-aware per `feedback_transactionscope_dapper.md`) so the
// service stays free of ADO.NET plumbing. State-flip + carton write
// methods are single-statement; they enlist on the ambient TX from
// PackTaskService.SubmitAsync alongside per-line updates and Carton
// INSERT (carton repo lives in CartonRepository — see same pattern as
// PickTaskRepository's MarkPicked-on-OrderAllocation).
internal sealed class PackTaskRepository : IPackTaskRepository
{
    private const string HeaderColumns = @"
        Id, PackNumber, SalesOrderId, Status, AssignedTo, Notes,
        GeneratedAt, GeneratedBy,
        PackedAt, PackedBy,
        CancelledAt, CancelledBy, CancelReason,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM outbound.PackTasks";

    private const string LineColumns = @"
        Id, PackTaskId, LineNumber, SalesOrderLineId,
        ProductId, OwnerId, UomId,
        PickedQuantity, PackedQuantity, LineStatus, ShortPackReason, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM outbound.PackTaskLines";

    private const string CartonColumns = @"
        Id, CartonNumber, PackTaskId, BoxTypeId, WeightKg, ShipmentId, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM outbound.Cartons";

    private readonly IDbConnection _connection;

    public PackTaskRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        PackTask header,
        IReadOnlyList<PackTaskLine> lines,
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
                @"INSERT INTO outbound.PackTasks
                      (Id, PackNumber, SalesOrderId, Status,
                       AssignedTo, Notes,
                       GeneratedBy, CreatedBy)
                  VALUES
                      (@Id, @PackNumber, @SalesOrderId, @Status,
                       @AssignedTo, @Notes,
                       @UserId, @UserId);",
                new
                {
                    header.Id,
                    header.PackNumber,
                    header.SalesOrderId,
                    header.Status,
                    header.AssignedTo,
                    header.Notes,
                    UserId = userId,
                },
                transaction: tx,
                cancellationToken: ct));

            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO outbound.PackTaskLines
                          (Id, PackTaskId, LineNumber, SalesOrderLineId,
                           ProductId, OwnerId, UomId,
                           PickedQuantity, LineStatus, Notes, CreatedBy)
                      VALUES
                          (@Id, @PackTaskId, @LineNumber, @SalesOrderLineId,
                           @ProductId, @OwnerId, @UomId,
                           @PickedQuantity, @LineStatus, @Notes, @UserId);",
                    new
                    {
                        line.Id,
                        line.PackTaskId,
                        line.LineNumber,
                        line.SalesOrderLineId,
                        line.ProductId,
                        line.OwnerId,
                        line.UomId,
                        line.PickedQuantity,
                        line.LineStatus,
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

    public Task<PackTaskDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE PackTaskId = @id ORDER BY LineNumber;
              SELECT " + CartonColumns + @" WHERE PackTaskId = @id;",
            new { id }, ct);

    public Task<PackTaskDetail?> GetByNumberAsync(
        string packNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM outbound.PackTasks WHERE PackNumber = @packNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE PackTaskId = @id ORDER BY LineNumber;
              SELECT " + CartonColumns + @" WHERE PackTaskId = @id;",
            new { packNumber }, ct);

    private async Task<PackTaskDetail?> ReadDetailAsync(
        string sql, object args, CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));
        var header = await multi.ReadSingleOrDefaultAsync<PackTask?>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<PackTaskLine>()).AsList();
        var carton = await multi.ReadSingleOrDefaultAsync<Carton?>();
        return new PackTaskDetail(header, lines, carton);
    }

    public async Task<PackTask?> GetActiveBySalesOrderAsync(
        Guid salesOrderId, CancellationToken ct = default)
    {
        // Active = Pending only (Pack has no InProgress for MVP).
        // Service uses this as the pre-generation guard.
        const string sql = @"
SELECT TOP (1) " + HeaderColumns + @"
WHERE SalesOrderId = @soId
  AND Status = 'Pending'
ORDER BY GeneratedAt DESC;";

        return await _connection.QuerySingleOrDefaultAsync<PackTask?>(
            new CommandDefinition(sql, new { soId = salesOrderId }, cancellationToken: ct));
    }

    public async Task<bool> SetPackedAsync(
        Guid packTaskId, Guid? userId, CancellationToken ct = default)
    {
        // Pending → Packed. CK_PackTasks_AuditMatchesStatus requires
        // PackedAt populated + CancelledAt NULL on this branch.
        const string sql = @"
UPDATE outbound.PackTasks
SET Status    = 'Packed',
    PackedAt  = SYSUTCDATETIME(),
    PackedBy  = @UserId,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @UserId,
    Version   = Version + 1
WHERE Id = @Id AND Status = 'Pending';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = packTaskId, UserId = userId },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetCancelledAsync(
        Guid packTaskId, string reason, Guid? userId,
        CancellationToken ct = default)
    {
        // Pending → Cancelled. Idempotent via WHERE Status='Pending'
        // (already-Cancelled returns 0 rows). CK_*_AuditMatchesStatus
        // requires CancelledAt populated + PackedAt NULL.
        const string sql = @"
UPDATE outbound.PackTasks
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
            new { Id = packTaskId, Reason = reason, UserId = userId },
            cancellationToken: ct));
        return rows > 0;
    }

    public Task UpdateLinePackedAsync(
        Guid lineId,
        decimal? packedQuantity,
        string lineStatus,
        string? shortPackReason,
        string? notes,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE outbound.PackTaskLines
              SET PackedQuantity   = @PackedQuantity,
                  LineStatus       = @LineStatus,
                  ShortPackReason  = @ShortPackReason,
                  Notes            = @Notes,
                  UpdatedAt        = SYSUTCDATETIME(),
                  UpdatedBy        = @UserId
              WHERE Id = @LineId;",
            new
            {
                LineId = lineId,
                PackedQuantity = packedQuantity,
                LineStatus = lineStatus,
                ShortPackReason = shortPackReason,
                Notes = notes,
                UserId = userId,
            },
            cancellationToken: ct));

    public Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM outbound.PackTasks
              WHERE PackNumber LIKE @prefix + '%';",
            new { prefix = datePrefix },
            cancellationToken: ct));

    public async Task<PagedResult<PackTaskListRow>> GetPagedAsync(
        PackTaskFilter f, CancellationToken ct = default)
    {
        var orderBy = PackTaskSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip = (f.Page - 1) * f.PageSize;
        var take = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@Status     IS NULL OR pt.Status = @Status)
  AND (@SearchLike IS NULL OR pt.PackNumber LIKE @SearchLike OR so.SoNumber LIKE @SearchLike)";

        var sql = $@"
WITH agg AS (
    SELECT PackTaskId, COUNT(*) AS LineCount
    FROM outbound.PackTaskLines
    GROUP BY PackTaskId
)
SELECT
    pt.Id, pt.PackNumber,
    pt.SalesOrderId, so.SoNumber,
    c.Code AS CustomerCode, c.Name AS CustomerName,
    pt.Status,
    ISNULL(agg.LineCount, 0) AS LineCount,
    pt.GeneratedAt,
    COALESCE(u.FullName, u.Email, 'System') AS GeneratedByName,
    pt.PackedAt,
    pt.CancelledAt
FROM outbound.PackTasks pt
JOIN outbound.SalesOrders so ON so.Id = pt.SalesOrderId
JOIN master.Customers     c  ON c.Id  = so.CustomerId
LEFT JOIN security.Users  u  ON u.Id  = pt.GeneratedBy
LEFT JOIN agg ON agg.PackTaskId = pt.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM outbound.PackTasks pt
JOIN outbound.SalesOrders so ON so.Id = pt.SalesOrderId
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

        var items = (await multi.ReadAsync<PackTaskListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<PackTaskListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<PackTaskStatusCounts> GetStatusCountsAsync(
        PackTaskFilter f, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                   AS [All],
    SUM(CASE WHEN pt.Status = 'Pending'   THEN 1 ELSE 0 END)  AS Pending,
    SUM(CASE WHEN pt.Status = 'Packed'    THEN 1 ELSE 0 END)  AS Packed,
    SUM(CASE WHEN pt.Status = 'Cancelled' THEN 1 ELSE 0 END)  AS Cancelled
FROM outbound.PackTasks pt
JOIN outbound.SalesOrders so ON so.Id = pt.SalesOrderId
WHERE (@SearchLike IS NULL OR pt.PackNumber LIKE @SearchLike OR so.SoNumber LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<PackTaskStatusCounts>(
            new CommandDefinition(sql, new { SearchLike = searchLike }, cancellationToken: ct));
    }
}
