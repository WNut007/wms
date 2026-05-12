using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader/writer for master.UnitsOfMeasure. Bound to a
// single tenant DB connection in ctor; factory creates one per
// tenantId via ITenantConnectionFactory.
internal sealed class UomRepository : IUomRepository
{
    private const string EntitySelect = @"
        SELECT Id, Code, Name, Type, IsBase, IsActive,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM master.UnitsOfMeasure";

    private readonly IDbConnection _connection;

    public UomRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // IX_UnitsOfMeasure_Active(IsActive, Code) covers WHERE+ORDER
        // exactly.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.UnitsOfMeasure
              WHERE IsActive = 1
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task<Uom?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Uom?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Uom?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Uom?>(new CommandDefinition(
            EntitySelect + " WHERE Code = @code",
            new { code },
            cancellationToken: ct));

    public Task<Uom?> GetBaseByTypeAsync(string type, Guid? exceptId, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Uom?>(new CommandDefinition(
            EntitySelect + @"
            WHERE Type = @type
              AND IsBase = 1
              AND (@exceptId IS NULL OR Id <> @exceptId);",
            new { type, exceptId },
            cancellationToken: ct));

    public async Task<Guid> InsertAsync(Uom entity, Guid? userId, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        const string sql = @"
INSERT INTO master.UnitsOfMeasure
    (Id, Code, Name, Type, IsBase, IsActive,
     CreatedAt, CreatedBy)
VALUES
    (@Id, @Code, @Name, @Type, @IsBase, @IsActive,
     SYSUTCDATETIME(), @UserId);";

        await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Code,
                entity.Name,
                entity.Type,
                entity.IsBase,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(Uom entity, Guid? userId, CancellationToken ct = default)
    {
        // Code + Type omitted — both read-only on Edit. Type lock
        // protects the "one base per Type" rule (flipping Type +
        // IsBase could orphan an existing base or duplicate one).
        const string sql = @"
UPDATE master.UnitsOfMeasure SET
    Name      = @Name,
    IsBase    = @IsBase,
    IsActive  = @IsActive,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @UserId
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Name,
                entity.IsBase,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<PagedResult<UomListRow>> GetPagedAsync(
        UomFilter f, CancellationToken ct = default)
    {
        var orderBy = UomSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip    = (f.Page - 1) * f.PageSize;
        var take    = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@IsActive IS NULL OR u.IsActive = @IsActive)
  AND (@Type     IS NULL OR u.Type     = @Type)
  AND (@SearchLike IS NULL
       OR u.Code LIKE @SearchLike
       OR u.Name LIKE @SearchLike)";

        var sql = $@"
SELECT
    u.Id, u.Code, u.Name, u.Type, u.IsBase, u.IsActive,
    u.CreatedAt, u.UpdatedAt
FROM master.UnitsOfMeasure u
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.UnitsOfMeasure u
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

        var items = (await multi.ReadAsync<UomListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<UomListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<UomStatusCounts> GetStatusCountsAsync(
        string? search, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

        // Single SUM(CASE) aggregate respects Search but ignores IsActive
        // and Type so inactive chips still display per-bucket totals.
        const string sql = @"
SELECT
    COUNT(*)                                                                  AS [All],
    ISNULL(SUM(CASE WHEN u.IsActive = 1 THEN 1 ELSE 0 END), 0)                AS [Active],
    ISNULL(SUM(CASE WHEN u.IsActive = 0 THEN 1 ELSE 0 END), 0)                AS [Inactive],
    ISNULL(SUM(CASE WHEN u.Type = 'Count'  THEN 1 ELSE 0 END), 0)             AS [Count],
    ISNULL(SUM(CASE WHEN u.Type = 'Weight' THEN 1 ELSE 0 END), 0)             AS [Weight],
    ISNULL(SUM(CASE WHEN u.Type = 'Volume' THEN 1 ELSE 0 END), 0)             AS [Volume],
    ISNULL(SUM(CASE WHEN u.Type = 'Length' THEN 1 ELSE 0 END), 0)             AS [Length]
FROM master.UnitsOfMeasure u
WHERE (@SearchLike IS NULL OR u.Code LIKE @SearchLike OR u.Name LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<UomStatusCounts>(new CommandDefinition(
            sql, new { SearchLike = searchLike }, cancellationToken: ct));
    }
}
