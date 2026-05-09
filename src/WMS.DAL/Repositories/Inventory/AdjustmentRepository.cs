using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Phase 11A — Dapper-backed Adjustment repo. State transitions are
// atomic single-statement UPDATEs (Idempotent via WHERE Status='Pending');
// the service composes them inside a TransactionScope alongside Stock
// writes (same pattern as Phase 10B cancellation).
internal sealed class AdjustmentRepository : IAdjustmentRepository
{
    // Reused across all read flavours — Dapper materialises by name.
    private const string SelectColumns = @"
        SELECT
            Id, AdjustmentNumber,
            StockId, LocationId, ProductId, LotId, PalletId, OwnerId, UomId, WarehouseId,
            QuantityDelta, Reason, Notes, Status,
            RequestedBy, RequestedAt, ApprovedBy, ApprovedAt, AppliedAt,
            RejectedBy, RejectedAt, RejectionReason,
            CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inventory.Adjustments";

    private readonly IDbConnection _connection;

    public AdjustmentRepository(IDbConnection connection) => _connection = connection;

    public Task CreateAsync(
        Adjustment e, Guid requestedBy, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO inventory.Adjustments
                  (Id, AdjustmentNumber,
                   StockId, LocationId, ProductId, LotId, PalletId, OwnerId, UomId, WarehouseId,
                   QuantityDelta, Reason, Notes, Status,
                   RequestedBy, CreatedBy)
              VALUES
                  (@Id, @AdjustmentNumber,
                   @StockId, @LocationId, @ProductId, @LotId, @PalletId, @OwnerId, @UomId, @WarehouseId,
                   @QuantityDelta, @Reason, @Notes, @Status,
                   @RequestedBy, @RequestedBy);",
            new
            {
                e.Id,
                e.AdjustmentNumber,
                e.StockId,
                e.LocationId,
                e.ProductId,
                e.LotId,
                e.PalletId,
                e.OwnerId,
                e.UomId,
                e.WarehouseId,
                e.QuantityDelta,
                e.Reason,
                e.Notes,
                e.Status,
                RequestedBy = requestedBy,
            },
            cancellationToken: ct));

    public Task<Adjustment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Adjustment?>(new CommandDefinition(
            SelectColumns + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Adjustment?> GetByNumberAsync(string adjustmentNumber, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Adjustment?>(new CommandDefinition(
            SelectColumns + " WHERE AdjustmentNumber = @adjustmentNumber",
            new { adjustmentNumber },
            cancellationToken: ct));

    public async Task<PagedResult<AdjustmentListRow>> GetPagedAsync(
        AdjustmentFilter f, CancellationToken ct = default)
    {
        var orderBy = AdjustmentSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip = (f.Page - 1) * f.PageSize;
        var take = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@Status        IS NULL OR a.Status        = @Status)
  AND (@Reason        IS NULL OR a.Reason        = @Reason)
  AND (@WarehouseCode IS NULL OR wh.Code         = @WarehouseCode)
  AND (@SearchLike    IS NULL
       OR a.AdjustmentNumber LIKE @SearchLike
       OR p.Code            LIKE @SearchLike)";

        var sql = $@"
SELECT
    a.Id, a.AdjustmentNumber,
    a.ProductId,   p.Code   AS ProductCode,    p.Name AS ProductName,
    a.WarehouseId, wh.Code  AS WarehouseCode,
    a.LocationId,  loc.Code AS LocationCode,
    a.OwnerId,     ow.Code  AS OwnerCode,
    u.Code        AS UomCode,
    a.QuantityDelta, a.Reason, a.Status,
    COALESCE(usr.FullName, usr.Email, 'System') AS RequestedByName,
    a.RequestedAt
FROM inventory.Adjustments a
JOIN master.Products       p   ON p.Id   = a.ProductId
JOIN master.Warehouses     wh  ON wh.Id  = a.WarehouseId
JOIN master.Locations      loc ON loc.Id = a.LocationId
JOIN master.Owners         ow  ON ow.Id  = a.OwnerId
JOIN master.UnitsOfMeasure u   ON u.Id   = a.UomId
LEFT JOIN security.Users   usr ON usr.Id = a.RequestedBy
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM inventory.Adjustments a
JOIN master.Products   p  ON p.Id  = a.ProductId
JOIN master.Warehouses wh ON wh.Id = a.WarehouseId
{whereClause};";

        var args = new
        {
            f.Status,
            f.Reason,
            f.WarehouseCode,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<AdjustmentListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<AdjustmentListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<AdjustmentStatusCounts> GetStatusCountsAsync(
        AdjustmentFilter f, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                  AS [All],
    SUM(CASE WHEN a.Status = 'Pending'  THEN 1 ELSE 0 END)    AS Pending,
    SUM(CASE WHEN a.Status = 'Applied'  THEN 1 ELSE 0 END)    AS Applied,
    SUM(CASE WHEN a.Status = 'Rejected' THEN 1 ELSE 0 END)    AS Rejected
FROM inventory.Adjustments a
JOIN master.Products   p  ON p.Id  = a.ProductId
JOIN master.Warehouses wh ON wh.Id = a.WarehouseId
WHERE (@Reason        IS NULL OR a.Reason = @Reason)
  AND (@WarehouseCode IS NULL OR wh.Code  = @WarehouseCode)
  AND (@SearchLike    IS NULL
       OR a.AdjustmentNumber LIKE @SearchLike
       OR p.Code            LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<AdjustmentStatusCounts>(
            new CommandDefinition(
                sql,
                new { f.Reason, f.WarehouseCode, SearchLike = searchLike },
                cancellationToken: ct));
    }

    public async Task<bool> SetAppliedAsync(
        Guid adjustmentId, Guid stockId, Guid approvedBy, CancellationToken ct = default)
    {
        // Idempotent: WHERE Status='Pending' returns 0 rows on already-
        // Applied / Rejected. CK_Adjustments_AuditMatchesStatus would
        // reject any partial-update so we set all 3 audit fields atomically.
        const string sql = @"
UPDATE inventory.Adjustments
SET Status     = 'Applied',
    StockId    = @StockId,
    ApprovedBy = @ApprovedBy,
    ApprovedAt = SYSUTCDATETIME(),
    AppliedAt  = SYSUTCDATETIME(),
    UpdatedAt  = SYSUTCDATETIME(),
    UpdatedBy  = @ApprovedBy,
    Version    = Version + 1
WHERE Id = @Id AND Status = 'Pending';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = adjustmentId, StockId = stockId, ApprovedBy = approvedBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetRejectedAsync(
        Guid adjustmentId, string reason, Guid rejectedBy, CancellationToken ct = default)
    {
        const string sql = @"
UPDATE inventory.Adjustments
SET Status          = 'Rejected',
    RejectedBy      = @RejectedBy,
    RejectedAt      = SYSUTCDATETIME(),
    RejectionReason = @Reason,
    UpdatedAt       = SYSUTCDATETIME(),
    UpdatedBy       = @RejectedBy,
    Version         = Version + 1
WHERE Id = @Id AND Status = 'Pending';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = adjustmentId, Reason = reason, RejectedBy = rejectedBy },
            cancellationToken: ct));
        return rows > 0;
    }

    public Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default) =>
        // datePrefix shape: 'ADJ-YYYYMMDD-' — service builds it. LIKE
        // matches any tail; counts existing rows for the day so the
        // service can assign the next sequential 4-digit suffix.
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM inventory.Adjustments
              WHERE AdjustmentNumber LIKE @prefix + '%';",
            new { prefix = datePrefix },
            cancellationToken: ct));
}
