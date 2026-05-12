using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader/writer for master.ProductCategories. Bound to a
// single tenant DB connection in ctor; factory creates one per tenantId
// via ITenantConnectionFactory.
//
// CRITICAL: INSERT / UPDATE SQL deliberately OMIT the Path column.
// trg_ProductCategories_MaintainPath (migration 031) is the single
// source of truth for Path — setting it from BLL would race the trigger
// or land wrong values if the trigger's CTE rebuilds it differently.
internal sealed class ProductCategoryRepository : IProductCategoryRepository
{
    private const string EntitySelect = @"
        SELECT Id, Code, Name, ParentId, Path, Description, DisplayOrder, IsActive,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM master.ProductCategories";

    private readonly IDbConnection _connection;

    public ProductCategoryRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // Path-ordered so the dropdown groups subtrees together.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.ProductCategories
              WHERE IsActive = 1
              ORDER BY CASE WHEN Path IS NULL THEN 1 ELSE 0 END, Path, Name",
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<ProductCategory?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<ProductCategory?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<ProductCategory?>(new CommandDefinition(
            EntitySelect + " WHERE Code = @code",
            new { code },
            cancellationToken: ct));

    public async Task<IReadOnlyList<ProductCategoryPickerItem>> GetPickerItemsAsync(
        Guid? excludeId, CancellationToken ct = default)
    {
        // Two-stage exclusion when excludeId is set:
        //   1. Exclude the row itself (Id = @excludeId).
        //   2. Exclude descendants: any row whose Path starts with the
        //      excluded row's Path + '/'.
        //
        // Path prefix-match LIKE pattern uses the row's Path + '/%' so
        // a sibling whose Path starts with the same string (e.g.
        // '/Skin' and '/Skincare') doesn't match — the trailing slash
        // anchors the boundary.
        const string sql = @"
WITH excl AS (
    SELECT Id, Path FROM master.ProductCategories WHERE Id = @excludeId
)
SELECT c.Id, c.Code, c.Name, c.Path
FROM master.ProductCategories c
WHERE c.IsActive = 1
  AND (@excludeId IS NULL
       OR (c.Id <> @excludeId
           AND NOT EXISTS (
               SELECT 1 FROM excl e
               WHERE e.Path IS NOT NULL
                 AND c.Path LIKE e.Path + '/%'
           )))
ORDER BY CASE WHEN c.Path IS NULL THEN 1 ELSE 0 END, c.Path, c.Name;";

        var rows = await _connection.QueryAsync<ProductCategoryPickerItem>(new CommandDefinition(
            sql, new { excludeId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Guid> InsertAsync(
        ProductCategory entity, Guid? userId, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        // Path INTENTIONALLY OMITTED — trigger maintains it.
        const string sql = @"
INSERT INTO master.ProductCategories
    (Id, Code, Name, ParentId, Description, DisplayOrder, IsActive,
     CreatedAt, CreatedBy)
VALUES
    (@Id, @Code, @Name, @ParentId, @Description, @DisplayOrder, @IsActive,
     SYSUTCDATETIME(), @UserId);";

        await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Code,
                entity.Name,
                entity.ParentId,
                entity.Description,
                entity.DisplayOrder,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(
        ProductCategory entity, Guid? userId, CancellationToken ct = default)
    {
        // Code omitted — read-only on Edit (FK orphan risk; Products
        // reference categories by Id but admin muscle-memory thinks
        // in Codes).
        //
        // Path INTENTIONALLY OMITTED — trigger updates it when Name
        // or ParentId changes (Migration_031 contract).
        const string sql = @"
UPDATE master.ProductCategories SET
    Name         = @Name,
    ParentId     = @ParentId,
    Description  = @Description,
    DisplayOrder = @DisplayOrder,
    IsActive     = @IsActive,
    UpdatedAt    = SYSUTCDATETIME(),
    UpdatedBy    = @UserId
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Name,
                entity.ParentId,
                entity.Description,
                entity.DisplayOrder,
                entity.IsActive,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<PagedResult<ProductCategoryListRow>> GetPagedAsync(
        ProductCategoryFilter f, CancellationToken ct = default)
    {
        var orderBy = ProductCategorySortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip    = (f.Page - 1) * f.PageSize;
        var take    = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        // ParentId filter (when set) overrides RootsOnly. Operator
        // wouldn't normally combine both — UI presents them as
        // mutually exclusive — but the filter survives either order.
        const string whereClause = @"
WHERE (@IsActive IS NULL OR c.IsActive = @IsActive)
  AND (@ParentId IS NULL OR c.ParentId = @ParentId)
  AND (@RootsOnly = 0 OR c.ParentId IS NULL)
  AND (@SearchLike IS NULL
       OR c.Code LIKE @SearchLike
       OR c.Name LIKE @SearchLike
       OR c.Path LIKE @SearchLike)";

        var sql = $@"
SELECT
    c.Id, c.Code, c.Name, c.ParentId,
    p.Name AS ParentName,
    c.Path, c.Description, c.DisplayOrder, c.IsActive,
    c.CreatedAt, c.UpdatedAt
FROM master.ProductCategories c
LEFT JOIN master.ProductCategories p ON p.Id = c.ParentId
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.ProductCategories c
{whereClause};";

        var args = new
        {
            f.IsActive,
            f.ParentId,
            RootsOnly = f.RootsOnly,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<ProductCategoryListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<ProductCategoryListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<ProductCategoryStatusCounts> GetStatusCountsAsync(
        string? search, CancellationToken ct = default)
    {
        var searchLike = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

        const string sql = @"
SELECT
    COUNT(*)                                                                  AS [All],
    ISNULL(SUM(CASE WHEN c.IsActive = 1     THEN 1 ELSE 0 END), 0)            AS [Active],
    ISNULL(SUM(CASE WHEN c.IsActive = 0     THEN 1 ELSE 0 END), 0)            AS [Inactive],
    ISNULL(SUM(CASE WHEN c.ParentId IS NULL THEN 1 ELSE 0 END), 0)            AS [Roots]
FROM master.ProductCategories c
WHERE (@SearchLike IS NULL
       OR c.Code LIKE @SearchLike
       OR c.Name LIKE @SearchLike
       OR c.Path LIKE @SearchLike);";

        return await _connection.QuerySingleAsync<ProductCategoryStatusCounts>(new CommandDefinition(
            sql, new { SearchLike = searchLike }, cancellationToken: ct));
    }
}
