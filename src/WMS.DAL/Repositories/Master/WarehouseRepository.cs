using System.Data;
using Dapper;
using WMS.Common.Auth;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader for master.Warehouses. Bound to a single tenant
// DB connection in its ctor; the factory creates one per tenantId via
// ITenantConnectionFactory.
//
// Two query shapes coexist intentionally:
//   - GetActiveAsync → 3-column WarehouseInfo projection (login picker)
//   - GetPagedAsync / GetByCode / GetById → full Warehouse entity +
//     WarehouseListRow with LocationCount aggregate (Phase 6B UI)
//
// SELECT lists omit Version — master.Warehouses has no Version column
// (BaseEntity.Version stays at default 0).
internal sealed class WarehouseRepository : IWarehouseRepository
{
    private const string EntitySelect = @"
        SELECT Id, Code, Name, Address, Type,
               PhoneNumber, ManagerName, TimeZoneId, IsActive,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM master.Warehouses";

    private readonly IDbConnection _connection;

    public WarehouseRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<WarehouseInfo>> GetActiveAsync(CancellationToken ct = default)
    {
        var rows = await _connection.QueryAsync<WarehouseInfo>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Warehouses
              WHERE IsActive = 1
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Warehouse?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Warehouse?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Warehouse?>(new CommandDefinition(
            EntitySelect + " WHERE Code = @code",
            new { code },
            cancellationToken: ct));

    public Task<WarehouseListRow?> GetListRowByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<WarehouseListRow?>(new CommandDefinition(
            // Same CTE + LEFT JOIN shape as GetPagedAsync — no
            // pagination wrapper, single-row WHERE on Code.
            @"WITH locs AS (
    SELECT WarehouseId, COUNT(*) AS Cnt
    FROM master.Locations
    GROUP BY WarehouseId
)
SELECT
    w.Id, w.Code, w.Name, w.Type, w.IsActive,
    ISNULL(l.Cnt, 0) AS LocationCount,
    w.Address, w.ManagerName, w.PhoneNumber,
    w.CreatedAt, w.UpdatedAt
FROM master.Warehouses w
LEFT JOIN locs l ON l.WarehouseId = w.Id
WHERE w.Code = @code",
            new { code },
            cancellationToken: ct));

    public async Task<Guid> InsertAsync(
        Warehouse entity, Guid? userId, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        const string sql = @"
INSERT INTO master.Warehouses
    (Id, Code, Name, Address, Type,
     PhoneNumber, ManagerName, TimeZoneId, IsActive,
     CreatedAt, CreatedBy)
VALUES
    (@Id, @Code, @Name, @Address, @Type,
     @PhoneNumber, @ManagerName, @TimeZoneId, @IsActive,
     SYSUTCDATETIME(), @UserId);";

        await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Code,
                entity.Name,
                entity.Address,
                entity.Type,
                entity.PhoneNumber,
                entity.ManagerName,
                entity.TimeZoneId,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(
        Warehouse entity, Guid? userId, CancellationToken ct = default)
    {
        // Code intentionally omitted — read-only on Edit. IsActive
        // updatable: toggling it off is the canonical "archive"
        // (TD-009: the mock UI's third "maintenance" state isn't in
        // the schema yet).
        const string sql = @"
UPDATE master.Warehouses SET
    Name        = @Name,
    Address     = @Address,
    Type        = @Type,
    PhoneNumber = @PhoneNumber,
    ManagerName = @ManagerName,
    TimeZoneId  = @TimeZoneId,
    IsActive    = @IsActive,
    UpdatedAt   = SYSUTCDATETIME(),
    UpdatedBy   = @UserId
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Name,
                entity.Address,
                entity.Type,
                entity.PhoneNumber,
                entity.ManagerName,
                entity.TimeZoneId,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<PagedResult<WarehouseListRow>> GetPagedAsync(
        WarehouseFilter f, CancellationToken ct = default)
    {
        var orderBy = WarehouseSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip    = (f.Page - 1) * f.PageSize;
        var take    = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        // Same WHERE shared between page query and COUNT — single source
        // of truth so they can never drift.
        const string whereClause = @"
WHERE (@IsActive IS NULL OR w.IsActive = @IsActive)
  AND (@Type IS NULL OR w.Type = @Type)
  AND (@SearchLike IS NULL
       OR w.Code    LIKE @SearchLike
       OR w.Name    LIKE @SearchLike
       OR w.Address LIKE @SearchLike)";

        // CTE counts ALL locations (not just IsActive=1) — matches the
        // mock's behaviour where 'LocationCount' answered "did this
        // warehouse get provisioned?". If active-only counting is
        // wanted later, add a WHERE l.IsActive = 1 inside the CTE.
        var sql = $@"
WITH locs AS (
    SELECT WarehouseId, COUNT(*) AS Cnt
    FROM master.Locations
    GROUP BY WarehouseId
)
SELECT
    w.Id, w.Code, w.Name, w.Type, w.IsActive,
    ISNULL(l.Cnt, 0) AS LocationCount,
    w.Address, w.ManagerName, w.PhoneNumber,
    w.CreatedAt, w.UpdatedAt
FROM master.Warehouses w
LEFT JOIN locs l ON l.WarehouseId = w.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.Warehouses w
{whereClause};";

        var args = new
        {
            f.IsActive,
            f.Type,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<WarehouseListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<WarehouseListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }
}
