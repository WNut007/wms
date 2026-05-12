using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader/writer for master.Zones. Bound to a single
// tenant DB connection in ctor; factory creates one per tenantId via
// ITenantConnectionFactory.
//
// master.Zones has CreatedAt + UpdatedAt only (no CreatedBy/UpdatedBy
// columns — Migration_006 pre-dates the Phase 7 audit pair convention).
// INSERT/UPDATE SQL omits the user columns; userId param accepted for
// signature symmetry but ignored.
internal sealed class ZoneRepository : IZoneRepository
{
    private const string EntitySelect = @"
        SELECT Id, WarehouseId, Code, Name, Type, Temperature,
               AllowedProductTypes, LotCommingleAllowed, IsActive,
               CreatedAt, UpdatedAt
        FROM master.Zones";

    private readonly IDbConnection _connection;

    public ZoneRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveByWarehouseAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        // IX_Zones_Warehouse(WarehouseId, IsActive) covers WHERE; ORDER
        // BY Code is a key-lookup but at <100 zones per warehouse the
        // cost is negligible.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Zones
              WHERE WarehouseId = @warehouseId AND IsActive = 1
              ORDER BY Code",
            new { warehouseId },
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task<Zone?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Zone?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Zone?> GetByCodeInWarehouseAsync(
        Guid warehouseId, string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Zone?>(new CommandDefinition(
            EntitySelect + " WHERE WarehouseId = @warehouseId AND Code = @code",
            new { warehouseId, code },
            cancellationToken: ct));

    public async Task<Guid> InsertAsync(Zone entity, Guid? userId, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        // No CreatedBy column on master.Zones — userId ignored.
        const string sql = @"
INSERT INTO master.Zones
    (Id, WarehouseId, Code, Name, Type, Temperature,
     AllowedProductTypes, LotCommingleAllowed, IsActive, CreatedAt)
VALUES
    (@Id, @WarehouseId, @Code, @Name, @Type, @Temperature,
     @AllowedProductTypes, @LotCommingleAllowed, @IsActive, SYSUTCDATETIME());";

        await _connection.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(Zone entity, Guid? userId, CancellationToken ct = default)
    {
        // Code + WarehouseId omitted — both read-only on Edit. Code is
        // part of the composite unique index; flipping WarehouseId
        // would orphan locations whose ZoneId points here.
        const string sql = @"
UPDATE master.Zones SET
    Name                = @Name,
    Type                = @Type,
    Temperature         = @Temperature,
    AllowedProductTypes = @AllowedProductTypes,
    LotCommingleAllowed = @LotCommingleAllowed,
    IsActive            = @IsActive,
    UpdatedAt           = SYSUTCDATETIME()
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<PagedResult<ZoneListRow>> GetPagedAsync(
        ZoneFilter f, CancellationToken ct = default)
    {
        var orderBy = ZoneSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip    = (f.Page - 1) * f.PageSize;
        var take    = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@IsActive    IS NULL OR z.IsActive    = @IsActive)
  AND (@WarehouseId IS NULL OR z.WarehouseId = @WarehouseId)
  AND (@Type        IS NULL OR z.Type        = @Type)
  AND (@SearchLike  IS NULL
       OR z.Code LIKE @SearchLike
       OR z.Name LIKE @SearchLike
       OR w.Code LIKE @SearchLike)";

        // LEFT JOIN locations CTE for the per-zone child count. Cheap
        // at <100 zones × <500 locations each per warehouse.
        var sql = $@"
WITH locs AS (
    SELECT ZoneId, COUNT(*) AS Cnt
    FROM master.Locations
    GROUP BY ZoneId
)
SELECT
    z.Id, z.WarehouseId,
    w.Code AS WarehouseCode, w.Name AS WarehouseName,
    z.Code, z.Name, z.Type, z.Temperature,
    z.LotCommingleAllowed, z.IsActive,
    ISNULL(l.Cnt, 0) AS LocationCount,
    z.CreatedAt, z.UpdatedAt
FROM master.Zones z
INNER JOIN master.Warehouses w ON w.Id = z.WarehouseId
LEFT JOIN locs l ON l.ZoneId = z.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.Zones z
INNER JOIN master.Warehouses w ON w.Id = z.WarehouseId
{whereClause};";

        var args = new
        {
            f.IsActive,
            f.WarehouseId,
            f.Type,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<ZoneListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<ZoneListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<ZoneStatusCounts> GetStatusCountsAsync(
        string? search, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                                AS [All],
    ISNULL(SUM(CASE WHEN z.IsActive = 1 THEN 1 ELSE 0 END), 0)              AS [Active],
    ISNULL(SUM(CASE WHEN z.IsActive = 0 THEN 1 ELSE 0 END), 0)              AS [Inactive],
    ISNULL(SUM(CASE WHEN z.Type = 'Receiving'  THEN 1 ELSE 0 END), 0)       AS [Receiving],
    ISNULL(SUM(CASE WHEN z.Type = 'Storage'    THEN 1 ELSE 0 END), 0)       AS [Storage],
    ISNULL(SUM(CASE WHEN z.Type = 'Picking'    THEN 1 ELSE 0 END), 0)       AS [Picking],
    ISNULL(SUM(CASE WHEN z.Type = 'Packing'    THEN 1 ELSE 0 END), 0)       AS [Packing],
    ISNULL(SUM(CASE WHEN z.Type = 'Shipping'   THEN 1 ELSE 0 END), 0)       AS [Shipping],
    ISNULL(SUM(CASE WHEN z.Type = 'Staging'    THEN 1 ELSE 0 END), 0)       AS [Staging],
    ISNULL(SUM(CASE WHEN z.Type = 'Quarantine' THEN 1 ELSE 0 END), 0)       AS [Quarantine],
    ISNULL(SUM(CASE WHEN z.Type = 'Returns'    THEN 1 ELSE 0 END), 0)       AS [Returns]
FROM master.Zones z
INNER JOIN master.Warehouses w ON w.Id = z.WarehouseId
WHERE (@SearchLike IS NULL
       OR z.Code LIKE @SearchLike
       OR z.Name LIKE @SearchLike
       OR w.Code LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<ZoneStatusCounts>(new CommandDefinition(
            sql, new { SearchLike = searchLike }, cancellationToken: ct));
    }
}
