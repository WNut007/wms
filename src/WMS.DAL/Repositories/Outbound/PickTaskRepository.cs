using System.Data;
using System.Transactions;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14C — Dapper-backed PickTask repo. CreateAsync owns a TX
// (ambient-aware per `feedback_transactionscope_dapper.md`) so the
// service stays free of ADO.NET plumbing. State-flip methods are
// single-statement; they enlist on the ambient TX from PickTaskService
// .SubmitAsync alongside Stock writes + OrderAllocation flips +
// SalesOrderLine.PickedQuantity bumps.
internal sealed class PickTaskRepository : IPickTaskRepository
{
    private const string HeaderColumns = @"
        Id, PickNumber, SalesOrderId, Status, AssignedTo, Notes,
        GeneratedAt, GeneratedBy,
        StartedAt, StartedBy,
        CompletedAt, CompletedBy,
        CancelledAt, CancelledBy, CancelReason,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM outbound.PickTasks";

    private const string LineColumns = @"
        Id, PickTaskId, LineNumber, OrderAllocationId,
        StockId, ProductId, OwnerId, UomId, LocationId, LotId, PalletId,
        ExpectedQuantity, PickedQuantity, LineStatus, ShortPickReason, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM outbound.PickTaskLines";

    private readonly IDbConnection _connection;

    public PickTaskRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        PickTask header,
        IReadOnlyList<PickTaskLine> lines,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        // TD-022 ambient-detection pattern (Phase 10B/13/14A precedent).
        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO outbound.PickTasks
                      (Id, PickNumber, SalesOrderId, Status,
                       AssignedTo, Notes,
                       GeneratedBy, CreatedBy)
                  VALUES
                      (@Id, @PickNumber, @SalesOrderId, @Status,
                       @AssignedTo, @Notes,
                       @UserId, @UserId);",
                new
                {
                    header.Id,
                    header.PickNumber,
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
                    @"INSERT INTO outbound.PickTaskLines
                          (Id, PickTaskId, LineNumber, OrderAllocationId,
                           StockId, ProductId, OwnerId, UomId, LocationId,
                           LotId, PalletId,
                           ExpectedQuantity, LineStatus, Notes, CreatedBy)
                      VALUES
                          (@Id, @PickTaskId, @LineNumber, @OrderAllocationId,
                           @StockId, @ProductId, @OwnerId, @UomId, @LocationId,
                           @LotId, @PalletId,
                           @ExpectedQuantity, @LineStatus, @Notes, @UserId);",
                    new
                    {
                        line.Id,
                        line.PickTaskId,
                        line.LineNumber,
                        line.OrderAllocationId,
                        line.StockId,
                        line.ProductId,
                        line.OwnerId,
                        line.UomId,
                        line.LocationId,
                        line.LotId,
                        line.PalletId,
                        line.ExpectedQuantity,
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

    public Task<PickTaskDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE PickTaskId = @id ORDER BY LineNumber;",
            new { id }, ct);

    public Task<PickTaskDetail?> GetByNumberAsync(
        string pickNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM outbound.PickTasks WHERE PickNumber = @pickNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE PickTaskId = @id ORDER BY LineNumber;",
            new { pickNumber }, ct);

    private async Task<PickTaskDetail?> ReadDetailAsync(
        string sql, object args, CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));
        var header = await multi.ReadSingleOrDefaultAsync<PickTask?>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<PickTaskLine>()).AsList();
        return new PickTaskDetail(header, lines);
    }

    public async Task<PickTask?> GetActiveBySalesOrderAsync(
        Guid salesOrderId, CancellationToken ct = default)
    {
        // Active = Pending OR InProgress. Service uses this as the pre-
        // generation guard so we don't double-task one SO. Returns at
        // most one — if more ever exist (data corruption) the service
        // will see the first by GeneratedAt DESC.
        const string sql = @"
SELECT TOP (1) " + HeaderColumns + @"
WHERE SalesOrderId = @soId
  AND Status IN ('Pending', 'InProgress')
ORDER BY GeneratedAt DESC;";

        return await _connection.QuerySingleOrDefaultAsync<PickTask?>(
            new CommandDefinition(sql, new { soId = salesOrderId }, cancellationToken: ct));
    }

    public async Task<bool> SetStartedAsync(
        Guid pickTaskId, Guid? userId, CancellationToken ct = default)
    {
        // Pending → InProgress. CK_PickTasks_AuditMatchesStatus
        // requires StartedAt populated on this branch.
        const string sql = @"
UPDATE outbound.PickTasks
SET Status    = 'InProgress',
    StartedAt = SYSUTCDATETIME(),
    StartedBy = @UserId,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @UserId,
    Version   = Version + 1
WHERE Id = @Id AND Status = 'Pending';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = pickTaskId, UserId = userId },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetCompletedAsync(
        Guid pickTaskId,
        string targetStatus,
        Guid? userId,
        CancellationToken ct = default)
    {
        // InProgress → Picked | PartiallyPicked. Service decides
        // targetStatus based on per-line aggregate (any line short
        // → PartiallyPicked, else Picked). CK_PickTasks_Status
        // restricts the closed enum; CK_*_AuditMatchesStatus
        // requires CompletedAt populated on either terminal.
        const string sql = @"
UPDATE outbound.PickTasks
SET Status      = @ToStatus,
    CompletedAt = SYSUTCDATETIME(),
    CompletedBy = @UserId,
    UpdatedAt   = SYSUTCDATETIME(),
    UpdatedBy   = @UserId,
    Version     = Version + 1
WHERE Id = @Id AND Status = 'InProgress';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = pickTaskId, ToStatus = targetStatus, UserId = userId },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetCancelledAsync(
        Guid pickTaskId,
        string fromStatus,
        string reason,
        Guid? userId,
        CancellationToken ct = default)
    {
        // Pending|InProgress → Cancelled. Caller picks the from
        // state for atomicity; the per-state UPDATE is idempotent
        // (already-Cancelled returns 0 rows). CK_*_AuditMatchesStatus
        // requires CancelledAt populated and CompletedAt NULL.
        const string sql = @"
UPDATE outbound.PickTasks
SET Status       = 'Cancelled',
    CancelledAt  = SYSUTCDATETIME(),
    CancelledBy  = @UserId,
    CancelReason = @Reason,
    UpdatedAt    = SYSUTCDATETIME(),
    UpdatedBy    = @UserId,
    Version      = Version + 1
WHERE Id = @Id AND Status = @FromStatus;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = pickTaskId,
                FromStatus = fromStatus,
                Reason = reason,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public Task UpdateLinePickedAsync(
        Guid lineId,
        decimal? pickedQuantity,
        string lineStatus,
        string? shortPickReason,
        string? notes,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE outbound.PickTaskLines
              SET PickedQuantity  = @PickedQuantity,
                  LineStatus      = @LineStatus,
                  ShortPickReason = @ShortPickReason,
                  Notes           = @Notes,
                  UpdatedAt       = SYSUTCDATETIME(),
                  UpdatedBy       = @UserId
              WHERE Id = @LineId;",
            new
            {
                LineId = lineId,
                PickedQuantity = pickedQuantity,
                LineStatus = lineStatus,
                ShortPickReason = shortPickReason,
                Notes = notes,
                UserId = userId,
            },
            cancellationToken: ct));

    public Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM outbound.PickTasks
              WHERE PickNumber LIKE @prefix + '%';",
            new { prefix = datePrefix },
            cancellationToken: ct));
}
