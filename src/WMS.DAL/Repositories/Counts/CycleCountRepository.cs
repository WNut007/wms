using System.Data;
using System.Transactions;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.DAL.Common;
using WMS.Domain.Entities.Counts;

namespace WMS.DAL.Repositories.Counts;

internal sealed class CycleCountRepository : ICycleCountRepository
{
    private const string HeaderColumns = @"
        Id, CountNumber, WarehouseId, LocationFilter, Status, Notes,
        StartedBy, StartedAt, CountedBy, CountedAt,
        ReviewedBy, ReviewedAt, AppliedAt,
        CancelledBy, CancelledAt, CancelReason,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM counts.CycleCounts";

    private const string LineColumns = @"
        Id, CycleCountId, LineNumber,
        StockId, LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
        ExpectedQuantity, CountedQuantity, LineStatus, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM counts.CycleCountLines";

    private readonly IDbConnection _connection;

    public CycleCountRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        CycleCount header,
        IReadOnlyList<CycleCountLine> lines,
        Guid startedBy,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        // Same TD-022 ambient-detection pattern as PO/Receiving repos.
        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO counts.CycleCounts
                      (Id, CountNumber, WarehouseId, LocationFilter, Status, Notes,
                       StartedBy, CreatedBy)
                  VALUES
                      (@Id, @CountNumber, @WarehouseId, @LocationFilter, @Status, @Notes,
                       @StartedBy, @StartedBy);",
                new
                {
                    header.Id,
                    header.CountNumber,
                    header.WarehouseId,
                    header.LocationFilter,
                    header.Status,
                    header.Notes,
                    StartedBy = startedBy,
                },
                transaction: tx,
                cancellationToken: ct));

            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO counts.CycleCountLines
                          (Id, CycleCountId, LineNumber,
                           StockId, LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
                           ExpectedQuantity, CountedQuantity, LineStatus, Notes,
                           CreatedBy)
                      VALUES
                          (@Id, @CycleCountId, @LineNumber,
                           @StockId, @LocationId, @ProductId, @LotId, @PalletId, @OwnerId, @UomId,
                           @ExpectedQuantity, @CountedQuantity, @LineStatus, @Notes,
                           @StartedBy);",
                    new
                    {
                        line.Id,
                        line.CycleCountId,
                        line.LineNumber,
                        line.StockId,
                        line.LocationId,
                        line.ProductId,
                        line.LotId,
                        line.PalletId,
                        line.OwnerId,
                        line.UomId,
                        line.ExpectedQuantity,
                        line.CountedQuantity,
                        line.LineStatus,
                        line.Notes,
                        StartedBy = startedBy,
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

    public Task<CycleCountDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE CycleCountId = @id ORDER BY LineNumber;",
            new { id },
            ct);

    public Task<CycleCountDetail?> GetByNumberAsync(string countNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM counts.CycleCounts WHERE CountNumber = @countNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE CycleCountId = @id ORDER BY LineNumber;",
            new { countNumber },
            ct);

    private async Task<CycleCountDetail?> ReadDetailAsync(
        string sql, object args, CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<CycleCount?>();
        if (header is null) return null;

        var lines = (await multi.ReadAsync<CycleCountLine>()).AsList();
        return new CycleCountDetail(header, lines);
    }

    public async Task<IReadOnlyList<CycleCountLineRow>> GetLineRowsByIdAsync(
        Guid cycleCountId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    cl.Id,
    cl.LineNumber,
    cl.StockId,
    cl.ProductId,
    p.Code        AS ProductCode,
    p.Name        AS ProductName,
    cl.LocationId,
    loc.Code      AS LocationCode,
    u.Code        AS UomCode,
    ow.Code       AS OwnerCode,
    lot.LotNumber AS LotNumber,
    pal.PalletNumber AS PalletNumber,
    cl.ExpectedQuantity,
    cl.CountedQuantity,
    cl.LineStatus,
    cl.Notes
FROM counts.CycleCountLines cl
JOIN master.Products        p   ON p.Id   = cl.ProductId
JOIN master.Locations       loc ON loc.Id = cl.LocationId
JOIN master.UnitsOfMeasure  u   ON u.Id   = cl.UomId
JOIN master.Owners          ow  ON ow.Id  = cl.OwnerId
LEFT JOIN inventory.Lots    lot ON lot.Id = cl.LotId
LEFT JOIN inventory.Pallets pal ON pal.Id = cl.PalletId
WHERE cl.CycleCountId = @cycleCountId
ORDER BY cl.LineNumber;";

        var rows = await _connection.QueryAsync<CycleCountLineRow>(new CommandDefinition(
            sql, new { cycleCountId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PagedResult<CycleCountListRow>> GetPagedAsync(
        CycleCountFilter f, CancellationToken ct = default)
    {
        var orderBy = CycleCountSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip = (f.Page - 1) * f.PageSize;
        var take = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@Status        IS NULL OR c.Status   = @Status)
  AND (@WarehouseCode IS NULL OR wh.Code    = @WarehouseCode)
  AND (@SearchLike    IS NULL OR c.CountNumber LIKE @SearchLike)";

        var sql = $@"
WITH agg AS (
    SELECT CycleCountId,
           COUNT(*)                                                AS LineCount,
           SUM(CASE WHEN LineStatus = 'Counted' THEN 1 ELSE 0 END) AS CountedLineCount,
           SUM(CASE WHEN LineStatus = 'Counted'
                    AND CountedQuantity <> ExpectedQuantity
                    THEN 1 ELSE 0 END)                             AS VarianceLineCount
    FROM counts.CycleCountLines
    GROUP BY CycleCountId
)
SELECT
    c.Id, c.CountNumber,
    c.WarehouseId, wh.Code AS WarehouseCode,
    locf.Code              AS LocationFilterCode,
    c.Status,
    ISNULL(agg.LineCount,         0) AS LineCount,
    ISNULL(agg.CountedLineCount,  0) AS CountedLineCount,
    ISNULL(agg.VarianceLineCount, 0) AS VarianceLineCount,
    COALESCE(u.FullName, u.Email, 'System') AS StartedByName,
    c.StartedAt
FROM counts.CycleCounts c
JOIN      master.Warehouses wh   ON wh.Id   = c.WarehouseId
LEFT JOIN master.Locations  locf ON locf.Id = c.LocationFilter
LEFT JOIN security.Users    u    ON u.Id    = c.StartedBy
LEFT JOIN agg ON agg.CycleCountId = c.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM counts.CycleCounts c
JOIN master.Warehouses wh ON wh.Id = c.WarehouseId
{whereClause};";

        var args = new { f.Status, f.WarehouseCode, SearchLike = searchLike, Skip = skip, Take = take };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<CycleCountListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<CycleCountListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<CycleCountStatusCounts> GetStatusCountsAsync(
        CycleCountFilter f, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(f.Search) ? null : $"%{f.Search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                  AS [All],
    SUM(CASE WHEN c.Status = 'Counting'  THEN 1 ELSE 0 END)   AS Counting,
    SUM(CASE WHEN c.Status = 'Review'    THEN 1 ELSE 0 END)   AS Review,
    SUM(CASE WHEN c.Status = 'Applied'   THEN 1 ELSE 0 END)   AS Applied,
    SUM(CASE WHEN c.Status = 'Cancelled' THEN 1 ELSE 0 END)   AS Cancelled
FROM counts.CycleCounts c
JOIN master.Warehouses wh ON wh.Id = c.WarehouseId
WHERE (@WarehouseCode IS NULL OR wh.Code = @WarehouseCode)
  AND (@SearchLike    IS NULL OR c.CountNumber LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<CycleCountStatusCounts>(
            new CommandDefinition(
                sql,
                new { f.WarehouseCode, SearchLike = searchLike },
                cancellationToken: ct));
    }

    public async Task SaveCountedQuantitiesAsync(
        Guid cycleCountId,
        IReadOnlyList<(Guid LineId, decimal? CountedQuantity, string LineStatus, string? Notes)> updates,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (updates.Count == 0) return;

        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        var hasAmbient = Transaction.Current is not null;
        using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();
        try
        {
            const string sql = @"
UPDATE counts.CycleCountLines
SET CountedQuantity = @CountedQuantity,
    LineStatus      = @LineStatus,
    Notes           = @Notes,
    UpdatedAt       = SYSUTCDATETIME(),
    UpdatedBy       = @UpdatedBy
WHERE Id = @LineId AND CycleCountId = @CycleCountId;";

            foreach (var u in updates)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        LineId = u.LineId,
                        CycleCountId = cycleCountId,
                        CountedQuantity = u.CountedQuantity,
                        LineStatus = u.LineStatus,
                        Notes = u.Notes,
                        UpdatedBy = currentUserId,
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

    public async Task<bool> SetSubmittedForReviewAsync(
        Guid cycleCountId, Guid countedBy, CancellationToken ct = default)
    {
        const string sql = @"
UPDATE counts.CycleCounts
SET Status     = 'Review',
    CountedBy  = @CountedBy,
    CountedAt  = SYSUTCDATETIME(),
    UpdatedAt  = SYSUTCDATETIME(),
    UpdatedBy  = @CountedBy,
    Version    = Version + 1
WHERE Id = @Id AND Status = 'Counting';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = cycleCountId, CountedBy = countedBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetAppliedAsync(
        Guid cycleCountId, Guid reviewedBy, CancellationToken ct = default)
    {
        const string sql = @"
UPDATE counts.CycleCounts
SET Status      = 'Applied',
    ReviewedBy  = @ReviewedBy,
    ReviewedAt  = SYSUTCDATETIME(),
    AppliedAt   = SYSUTCDATETIME(),
    UpdatedAt   = SYSUTCDATETIME(),
    UpdatedBy   = @ReviewedBy,
    Version     = Version + 1
WHERE Id = @Id AND Status = 'Review';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = cycleCountId, ReviewedBy = reviewedBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetCancelledAsync(
        Guid cycleCountId, string fromStatus, string reason, Guid cancelledBy,
        CancellationToken ct = default)
    {
        const string sql = @"
UPDATE counts.CycleCounts
SET Status       = 'Cancelled',
    CancelledBy  = @CancelledBy,
    CancelledAt  = SYSUTCDATETIME(),
    CancelReason = @Reason,
    UpdatedAt    = SYSUTCDATETIME(),
    UpdatedBy    = @CancelledBy,
    Version      = Version + 1
WHERE Id = @Id AND Status = @FromStatus;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = cycleCountId,
                FromStatus = fromStatus,
                Reason = reason,
                CancelledBy = cancelledBy,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM counts.CycleCounts
              WHERE CountNumber LIKE @prefix + '%';",
            new { prefix = datePrefix },
            cancellationToken: ct));
}
