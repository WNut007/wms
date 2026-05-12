using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader/writer for master.Locations. Bound to a single
// tenant DB connection in ctor; factory creates one per tenantId via
// ITenantConnectionFactory.
//
// master.Locations has full audit pair (CreatedBy/UpdatedBy) and BOTH
// a Status enum (operational: Active/Blocked/Maintenance) AND IsActive
// bool (soft-delete). They're orthogonal — a Blocked location can
// still be IsActive=1; an Inactive location archives the record
// regardless of Status.
internal sealed class LocationRepository : ILocationRepository
{
    private const string EntitySelect = @"
        SELECT Id, WarehouseId, ZoneId, Code, Description,
               LengthCm, WidthCm, HeightCm,
               CapacityVolumeCubicCm, CapacityWeightKg, CapacityPolicy,
               BinRank, AllowMultipleItems, MinPickQty, MaxPickQty,
               PositionX, PositionY, PositionZ, Rotation,
               Aisle, Bay, Level, Show3D, DisplayColor, IsPickface,
               Status, LastVerifiedAt, IsActive,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM master.Locations";

    private readonly IDbConnection _connection;

    public LocationRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveByWarehouseAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        // master.Locations has Code + Description (nullable) — no `Name`
        // column. LookupItem's 3-arg ctor expects (Id, Code, Name) so
        // we COALESCE Description → Code to satisfy the third parameter.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, COALESCE(Description, Code) AS Name
              FROM master.Locations
              WHERE WarehouseId = @warehouseId
                AND IsActive = 1
                AND Status = 'Active'
              ORDER BY Code",
            new { warehouseId },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<LookupItem>> GetZonesForWarehouseAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        // Cascade picker source for Locations Create/Edit form.
        // Same filter as IZoneRepository.GetActiveByWarehouseAsync —
        // duplicated here to avoid a cross-repo call in the controller's
        // AJAX endpoint.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Zones
              WHERE WarehouseId = @warehouseId AND IsActive = 1
              ORDER BY Code",
            new { warehouseId },
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task<Location?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Location?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Location?> GetByCodeInWarehouseAsync(
        Guid warehouseId, string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Location?>(new CommandDefinition(
            EntitySelect + " WHERE WarehouseId = @warehouseId AND Code = @code",
            new { warehouseId, code },
            cancellationToken: ct));

    public async Task<Guid> InsertAsync(Location entity, Guid? userId, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        const string sql = @"
INSERT INTO master.Locations
    (Id, WarehouseId, ZoneId, Code, Description,
     LengthCm, WidthCm, HeightCm,
     CapacityVolumeCubicCm, CapacityWeightKg, CapacityPolicy,
     BinRank, AllowMultipleItems, MinPickQty, MaxPickQty,
     PositionX, PositionY, PositionZ, Rotation,
     Aisle, Bay, Level, Show3D, DisplayColor, IsPickface,
     Status, IsActive,
     CreatedAt, CreatedBy)
VALUES
    (@Id, @WarehouseId, @ZoneId, @Code, @Description,
     @LengthCm, @WidthCm, @HeightCm,
     @CapacityVolumeCubicCm, @CapacityWeightKg, @CapacityPolicy,
     @BinRank, @AllowMultipleItems, @MinPickQty, @MaxPickQty,
     @PositionX, @PositionY, @PositionZ, @Rotation,
     @Aisle, @Bay, @Level, @Show3D, @DisplayColor, @IsPickface,
     @Status, @IsActive,
     SYSUTCDATETIME(), @UserId);";

        await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.WarehouseId,
                entity.ZoneId,
                entity.Code,
                entity.Description,
                entity.LengthCm,
                entity.WidthCm,
                entity.HeightCm,
                entity.CapacityVolumeCubicCm,
                entity.CapacityWeightKg,
                entity.CapacityPolicy,
                entity.BinRank,
                entity.AllowMultipleItems,
                entity.MinPickQty,
                entity.MaxPickQty,
                entity.PositionX,
                entity.PositionY,
                entity.PositionZ,
                entity.Rotation,
                entity.Aisle,
                entity.Bay,
                entity.Level,
                entity.Show3D,
                entity.DisplayColor,
                entity.IsPickface,
                entity.Status,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(Location entity, Guid? userId, CancellationToken ct = default)
    {
        // Code + WarehouseId omitted — read-only on Edit. ZoneId IS
        // editable (operator may reclassify a bin to a different zone
        // within the SAME warehouse; the picker constrains to current-
        // warehouse zones).
        const string sql = @"
UPDATE master.Locations SET
    ZoneId                = @ZoneId,
    Description           = @Description,
    LengthCm              = @LengthCm,
    WidthCm               = @WidthCm,
    HeightCm              = @HeightCm,
    CapacityVolumeCubicCm = @CapacityVolumeCubicCm,
    CapacityWeightKg      = @CapacityWeightKg,
    CapacityPolicy        = @CapacityPolicy,
    BinRank               = @BinRank,
    AllowMultipleItems    = @AllowMultipleItems,
    MinPickQty            = @MinPickQty,
    MaxPickQty            = @MaxPickQty,
    PositionX             = @PositionX,
    PositionY             = @PositionY,
    PositionZ             = @PositionZ,
    Rotation              = @Rotation,
    Aisle                 = @Aisle,
    Bay                   = @Bay,
    Level                 = @Level,
    Show3D                = @Show3D,
    DisplayColor          = @DisplayColor,
    IsPickface            = @IsPickface,
    Status                = @Status,
    IsActive              = @IsActive,
    UpdatedAt             = SYSUTCDATETIME(),
    UpdatedBy             = @UserId
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.ZoneId,
                entity.Description,
                entity.LengthCm,
                entity.WidthCm,
                entity.HeightCm,
                entity.CapacityVolumeCubicCm,
                entity.CapacityWeightKg,
                entity.CapacityPolicy,
                entity.BinRank,
                entity.AllowMultipleItems,
                entity.MinPickQty,
                entity.MaxPickQty,
                entity.PositionX,
                entity.PositionY,
                entity.PositionZ,
                entity.Rotation,
                entity.Aisle,
                entity.Bay,
                entity.Level,
                entity.Show3D,
                entity.DisplayColor,
                entity.IsPickface,
                entity.Status,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<PagedResult<LocationListRow>> GetPagedAsync(
        LocationFilter f, CancellationToken ct = default)
    {
        var orderBy = LocationSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip    = (f.Page - 1) * f.PageSize;
        var take    = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@IsActive    IS NULL OR l.IsActive    = @IsActive)
  AND (@WarehouseId IS NULL OR l.WarehouseId = @WarehouseId)
  AND (@ZoneId      IS NULL OR l.ZoneId      = @ZoneId)
  AND (@Status      IS NULL OR l.Status      = @Status)
  AND (@SearchLike  IS NULL
       OR l.Code        LIKE @SearchLike
       OR l.Description LIKE @SearchLike
       OR w.Code        LIKE @SearchLike
       OR z.Code        LIKE @SearchLike)";

        // LEFT JOIN inventory.Stock CTE for per-location stock-row count.
        // Empty bins show 0; bins with multiple SKUs show N.
        var sql = $@"
WITH stock AS (
    SELECT LocationId, COUNT(*) AS Cnt
    FROM inventory.Stock
    WHERE QuantityOnHand > 0
    GROUP BY LocationId
)
SELECT
    l.Id, l.WarehouseId, w.Code AS WarehouseCode,
    l.ZoneId, z.Code AS ZoneCode, z.Type AS ZoneType,
    l.Code, l.Description, l.CapacityPolicy, l.BinRank, l.IsPickface,
    l.Status, l.IsActive,
    ISNULL(s.Cnt, 0) AS StockRows,
    l.CreatedAt, l.UpdatedAt
FROM master.Locations l
INNER JOIN master.Warehouses w ON w.Id = l.WarehouseId
INNER JOIN master.Zones      z ON z.Id = l.ZoneId
LEFT JOIN stock s ON s.LocationId = l.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.Locations l
INNER JOIN master.Warehouses w ON w.Id = l.WarehouseId
INNER JOIN master.Zones      z ON z.Id = l.ZoneId
{whereClause};";

        var args = new
        {
            f.IsActive,
            f.WarehouseId,
            f.ZoneId,
            f.Status,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<LocationListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<LocationListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<LocationStatusCounts> GetStatusCountsAsync(
        string? search, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                                  AS [All],
    ISNULL(SUM(CASE WHEN l.IsActive = 1            THEN 1 ELSE 0 END), 0)     AS [Active],
    ISNULL(SUM(CASE WHEN l.IsActive = 0            THEN 1 ELSE 0 END), 0)     AS [Inactive],
    ISNULL(SUM(CASE WHEN l.Status = 'Active'       THEN 1 ELSE 0 END), 0)     AS [StatusActive],
    ISNULL(SUM(CASE WHEN l.Status = 'Blocked'      THEN 1 ELSE 0 END), 0)     AS [StatusBlocked],
    ISNULL(SUM(CASE WHEN l.Status = 'Maintenance'  THEN 1 ELSE 0 END), 0)     AS [StatusMaintenance],
    ISNULL(SUM(CASE WHEN l.IsPickface = 1          THEN 1 ELSE 0 END), 0)     AS [Pickfaces]
FROM master.Locations l
INNER JOIN master.Warehouses w ON w.Id = l.WarehouseId
INNER JOIN master.Zones      z ON z.Id = l.ZoneId
WHERE (@SearchLike IS NULL
       OR l.Code        LIKE @SearchLike
       OR l.Description LIKE @SearchLike
       OR w.Code        LIKE @SearchLike
       OR z.Code        LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<LocationStatusCounts>(new CommandDefinition(
            sql, new { SearchLike = searchLike }, cancellationToken: ct));
    }
}
