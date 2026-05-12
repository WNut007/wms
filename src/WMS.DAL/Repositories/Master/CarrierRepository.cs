using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader/writer for master.Carriers. Bound to a single
// tenant DB connection in ctor; factory creates one per tenantId via
// ITenantConnectionFactory.
internal sealed class CarrierRepository : ICarrierRepository
{
    private const string EntitySelect = @"
        SELECT Id, Code, Name, Type, Country, Status,
               SupportedServiceLevels, WebsiteUrl, LogoUrl,
               DisplayOrder, IsActive,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM master.Carriers";

    private readonly IDbConnection _connection;

    public CarrierRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // Production-only — admins should not pick a Configured /
        // Tested carrier as a customer's preferred (would route real
        // shipments to a not-yet-validated integration).
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Carriers
              WHERE Status = 'Production' AND IsActive = 1
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task<Carrier?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Carrier?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Carrier?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Carrier?>(new CommandDefinition(
            EntitySelect + " WHERE Code = @code",
            new { code },
            cancellationToken: ct));

    public async Task<Guid> InsertAsync(Carrier entity, Guid? userId, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        const string sql = @"
INSERT INTO master.Carriers
    (Id, Code, Name, Type, Country, Status,
     SupportedServiceLevels, WebsiteUrl, LogoUrl,
     DisplayOrder, IsActive,
     CreatedAt, CreatedBy)
VALUES
    (@Id, @Code, @Name, @Type, @Country, @Status,
     @SupportedServiceLevels, @WebsiteUrl, @LogoUrl,
     @DisplayOrder, @IsActive,
     SYSUTCDATETIME(), @UserId);";

        await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Code,
                entity.Name,
                entity.Type,
                entity.Country,
                entity.Status,
                entity.SupportedServiceLevels,
                entity.WebsiteUrl,
                entity.LogoUrl,
                entity.DisplayOrder,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(Carrier entity, Guid? userId, CancellationToken ct = default)
    {
        // Code omitted — read-only on Edit (FK orphan risk;
        // customers.PreferredCarrierId references by Id but admin
        // muscle-memory thinks in Codes).
        //
        // Status IS editable here — the whole Block 1.3 point is that
        // operators can flip Inactive → Production via this form (per
        // Mod #2: no migration; the UI is the path).
        const string sql = @"
UPDATE master.Carriers SET
    Name                   = @Name,
    Type                   = @Type,
    Country                = @Country,
    Status                 = @Status,
    SupportedServiceLevels = @SupportedServiceLevels,
    WebsiteUrl             = @WebsiteUrl,
    LogoUrl                = @LogoUrl,
    DisplayOrder           = @DisplayOrder,
    IsActive               = @IsActive,
    UpdatedAt              = SYSUTCDATETIME(),
    UpdatedBy              = @UserId
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Name,
                entity.Type,
                entity.Country,
                entity.Status,
                entity.SupportedServiceLevels,
                entity.WebsiteUrl,
                entity.LogoUrl,
                entity.DisplayOrder,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<PagedResult<CarrierListRow>> GetPagedAsync(
        CarrierFilter f, CancellationToken ct = default)
    {
        var orderBy = CarrierSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip    = (f.Page - 1) * f.PageSize;
        var take    = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        const string whereClause = @"
WHERE (@Status   IS NULL OR c.Status   = @Status)
  AND (@IsActive IS NULL OR c.IsActive = @IsActive)
  AND (@Type     IS NULL OR c.Type     = @Type)
  AND (@SearchLike IS NULL
       OR c.Code LIKE @SearchLike
       OR c.Name LIKE @SearchLike)";

        var sql = $@"
SELECT
    c.Id, c.Code, c.Name, c.Type, c.Country, c.Status,
    c.DisplayOrder, c.IsActive, c.CreatedAt, c.UpdatedAt
FROM master.Carriers c
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.Carriers c
{whereClause};";

        var args = new
        {
            f.Status,
            f.IsActive,
            f.Type,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<CarrierListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<CarrierListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<CarrierStatusCounts> GetStatusCountsAsync(
        string? search, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

        // SUM(CASE) respects Search but ignores Status filter so
        // inactive chips still display per-status totals.
        const string sql = @"
SELECT
    COUNT(*)                                                                AS [All],
    ISNULL(SUM(CASE WHEN c.Status = 'Inactive'   THEN 1 ELSE 0 END), 0)     AS [Inactive],
    ISNULL(SUM(CASE WHEN c.Status = 'Configured' THEN 1 ELSE 0 END), 0)     AS [Configured],
    ISNULL(SUM(CASE WHEN c.Status = 'Tested'     THEN 1 ELSE 0 END), 0)     AS [Tested],
    ISNULL(SUM(CASE WHEN c.Status = 'Production' THEN 1 ELSE 0 END), 0)     AS [Production]
FROM master.Carriers c
WHERE (@SearchLike IS NULL OR c.Code LIKE @SearchLike OR c.Name LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<CarrierStatusCounts>(new CommandDefinition(
            sql, new { SearchLike = searchLike }, cancellationToken: ct));
    }
}
