using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader/writer for master.BoxTypes. Bound to a single
// tenant DB connection in ctor; factory creates one per tenantId via
// ITenantConnectionFactory.
//
// master.BoxTypes has NO CreatedBy/UpdatedBy/Version columns
// (Migration_010 shape). INSERT/UPDATE SQL omits them; the userId param
// is accepted for signature symmetry with other Master repos but ignored.
internal sealed class BoxTypeRepository : IBoxTypeRepository
{
    private const string EntitySelect = @"
        SELECT Id, Code, Name,
               InternalLengthCm, InternalWidthCm, InternalHeightCm,
               InternalVolumeCubicCm,
               ExternalLengthCm, ExternalWidthCm, ExternalHeightCm,
               EmptyWeightKg, MaxLoadKg, UnitCost,
               IsActive, CreatedAt, UpdatedAt
        FROM master.BoxTypes";

    private readonly IDbConnection _connection;

    public BoxTypeRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // IX_BoxTypes_Active(IsActive, Code) covers WHERE+ORDER exactly.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.BoxTypes
              WHERE IsActive = 1
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task<BoxType?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<BoxType?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<BoxType?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<BoxType?>(new CommandDefinition(
            EntitySelect + " WHERE Code = @code",
            new { code },
            cancellationToken: ct));

    public async Task<Guid> InsertAsync(BoxType entity, Guid? userId, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        const string sql = @"
INSERT INTO master.BoxTypes
    (Id, Code, Name,
     InternalLengthCm, InternalWidthCm, InternalHeightCm,
     InternalVolumeCubicCm,
     ExternalLengthCm, ExternalWidthCm, ExternalHeightCm,
     EmptyWeightKg, MaxLoadKg, UnitCost,
     IsActive, CreatedAt)
VALUES
    (@Id, @Code, @Name,
     @InternalLengthCm, @InternalWidthCm, @InternalHeightCm,
     @InternalVolumeCubicCm,
     @ExternalLengthCm, @ExternalWidthCm, @ExternalHeightCm,
     @EmptyWeightKg, @MaxLoadKg, @UnitCost,
     @IsActive, SYSUTCDATETIME());";

        await _connection.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(BoxType entity, Guid? userId, CancellationToken ct = default)
    {
        // Code omitted — read-only on Edit (FK orphan risk if changed).
        const string sql = @"
UPDATE master.BoxTypes SET
    Name                  = @Name,
    InternalLengthCm      = @InternalLengthCm,
    InternalWidthCm       = @InternalWidthCm,
    InternalHeightCm      = @InternalHeightCm,
    InternalVolumeCubicCm = @InternalVolumeCubicCm,
    ExternalLengthCm      = @ExternalLengthCm,
    ExternalWidthCm       = @ExternalWidthCm,
    ExternalHeightCm      = @ExternalHeightCm,
    EmptyWeightKg         = @EmptyWeightKg,
    MaxLoadKg             = @MaxLoadKg,
    UnitCost              = @UnitCost,
    IsActive              = @IsActive,
    UpdatedAt             = SYSUTCDATETIME()
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<PagedResult<BoxTypeListRow>> GetPagedAsync(
        BoxTypeFilter f, CancellationToken ct = default)
    {
        var orderBy = BoxTypeSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip    = (f.Page - 1) * f.PageSize;
        var take    = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@IsActive IS NULL OR b.IsActive = @IsActive)
  AND (@SearchLike IS NULL
       OR b.Code LIKE @SearchLike
       OR b.Name LIKE @SearchLike)";

        var sql = $@"
SELECT
    b.Id, b.Code, b.Name,
    b.InternalLengthCm, b.InternalWidthCm, b.InternalHeightCm,
    b.ExternalLengthCm, b.ExternalWidthCm, b.ExternalHeightCm,
    b.EmptyWeightKg, b.MaxLoadKg, b.UnitCost,
    b.IsActive, b.CreatedAt, b.UpdatedAt
FROM master.BoxTypes b
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.BoxTypes b
{whereClause};";

        var args = new
        {
            f.IsActive,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<BoxTypeListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<BoxTypeListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<BoxTypeStatusCounts> GetStatusCountsAsync(
        string? search, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

        // Single SUM(CASE) aggregate respects Search but ignores IsActive
        // so inactive chips still display per-status totals.
        const string sql = @"
SELECT
    COUNT(*)                                        AS [All],
    ISNULL(SUM(CASE WHEN b.IsActive = 1 THEN 1 ELSE 0 END), 0) AS [Active],
    ISNULL(SUM(CASE WHEN b.IsActive = 0 THEN 1 ELSE 0 END), 0) AS [Inactive]
FROM master.BoxTypes b
WHERE (@SearchLike IS NULL OR b.Code LIKE @SearchLike OR b.Name LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<BoxTypeStatusCounts>(new CommandDefinition(
            sql, new { SearchLike = searchLike }, cancellationToken: ct));
    }
}
