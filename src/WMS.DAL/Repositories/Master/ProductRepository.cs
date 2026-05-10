using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader for master.Products. Bound to a single tenant
// DB connection in its ctor; the factory creates one per tenantId via
// ITenantConnectionFactory. Mirrors WarehouseRepository's shape.
//
// SELECT lists omit Version — master.Products has no Version column
// (BaseEntity.Version stays at default 0; optimistic concurrency not
// used on master metadata).
internal sealed class ProductRepository : IProductRepository
{
    // Entity SELECT used by GetById / GetByCode. Column order matches the
    // Product entity property declaration order so Dapper materialises
    // by name without ambiguity.
    private const string EntitySelect = @"
        SELECT Id, Code, Name, CategoryId, BaseUomId,
               TrackingMethod, VelocityClass, UseCatchWeight, Status, Brand,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM master.Products";

    private readonly IDbConnection _connection;

    public ProductRepository(IDbConnection connection) => _connection = connection;

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Product?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Product?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Product?>(new CommandDefinition(
            EntitySelect + " WHERE Code = @code",
            new { code },
            cancellationToken: ct));

    public Task<ProductListRow?> GetListRowByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<ProductListRow?>(new CommandDefinition(
            // Same CTE + LEFT JOIN shape as GetPagedAsync — no
            // pagination wrapper, single-row WHERE on Code.
            @"WITH stock AS (
    SELECT ProductId, SUM(QuantityOnHand) AS Qty
    FROM inventory.Stock
    GROUP BY ProductId
)
SELECT
    p.Id, p.Code, p.Name, p.Status, p.Brand,
    p.CategoryId, c.Code AS CategoryCode,
    ISNULL(s.Qty, 0) AS StockOnHand,
    p.CreatedAt, p.UpdatedAt
FROM master.Products p
JOIN master.ProductCategories c ON c.Id = p.CategoryId
LEFT JOIN stock s ON s.ProductId = p.Id
WHERE p.Code = @code",
            new { code },
            cancellationToken: ct));

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // IX_Products_Status(Status, Code) covers WHERE+ORDER exactly.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Products
              WHERE Status = 'Active'
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Guid> InsertAsync(
        Product entity, Guid? userId, CancellationToken ct = default)
    {
        // Caller-assigned Id wins; otherwise generate one client-side
        // so subsequent reads (e.g. redirect to /Products/Detail/{code})
        // don't need a follow-up SELECT to discover it. Mirrors
        // DocumentRepository.InsertAsync.
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        // CreatedAt set server-side via SYSUTCDATETIME() so all rows
        // share a consistent clock; UpdatedAt left NULL on insert
        // (signals "never edited"). No Version column on master.Products.
        const string sql = @"
INSERT INTO master.Products
    (Id, Code, Name, CategoryId, BaseUomId,
     TrackingMethod, VelocityClass, UseCatchWeight, Status, Brand,
     CreatedAt, CreatedBy)
VALUES
    (@Id, @Code, @Name, @CategoryId, @BaseUomId,
     @TrackingMethod, @VelocityClass, @UseCatchWeight, @Status, @Brand,
     SYSUTCDATETIME(), @UserId);";

        await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Code,
                entity.Name,
                entity.CategoryId,
                entity.BaseUomId,
                entity.TrackingMethod,
                entity.VelocityClass,
                entity.UseCatchWeight,
                entity.Status,
                entity.Brand,
                UserId = userId,
            },
            cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(
        Product entity, Guid? userId, CancellationToken ct = default)
    {
        // Code intentionally omitted from SET — read-only on Edit per
        // Phase 7 brief. Changing it would orphan inventory.Stock,
        // billing, and movement-log references that key on ProductId
        // but display Code in audit trails.
        const string sql = @"
UPDATE master.Products SET
    Name           = @Name,
    CategoryId     = @CategoryId,
    BaseUomId      = @BaseUomId,
    TrackingMethod = @TrackingMethod,
    VelocityClass  = @VelocityClass,
    UseCatchWeight = @UseCatchWeight,
    Status         = @Status,
    Brand          = @Brand,
    UpdatedAt      = SYSUTCDATETIME(),
    UpdatedBy      = @UserId
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Name,
                entity.CategoryId,
                entity.BaseUomId,
                entity.TrackingMethod,
                entity.VelocityClass,
                entity.UseCatchWeight,
                entity.Status,
                entity.Brand,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<PagedResult<ProductListRow>> GetPagedAsync(
        ProductFilter f, CancellationToken ct = default)
    {
        // Sort column resolved via the closed-set whitelist — never
        // splices raw user input into ORDER BY.
        var orderBy   = ProductSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip      = (f.Page - 1) * f.PageSize;
        var take      = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        // Same WHERE clause used by the page query and the COUNT — keep
        // a single source of truth so they can never drift.
        const string whereClause = @"
WHERE (@Status IS NULL OR p.Status = @Status)
  AND (@CategoryCode IS NULL OR c.Code = @CategoryCode)
  AND (@Brand IS NULL OR p.Brand = @Brand)
  AND (@SearchLike IS NULL
       OR p.Code LIKE @SearchLike
       OR p.Name LIKE @SearchLike
       OR p.Brand LIKE @SearchLike)";

        // CTE materialises the per-product OnHand aggregate once, then
        // LEFT JOINs so products with no Stock rows show 0 (not NULL).
        // ISNULL preserves the column type as NOT NULL DECIMAL on the
        // wire — Dapper materialises into ProductListRow.StockOnHand
        // (decimal) without surprises.
        var sql = $@"
WITH stock AS (
    SELECT ProductId, SUM(QuantityOnHand) AS Qty
    FROM inventory.Stock
    GROUP BY ProductId
)
SELECT
    p.Id, p.Code, p.Name, p.Status, p.Brand,
    p.CategoryId, c.Code AS CategoryCode,
    ISNULL(s.Qty, 0) AS StockOnHand,
    p.CreatedAt, p.UpdatedAt
FROM master.Products p
JOIN master.ProductCategories c ON c.Id = p.CategoryId
LEFT JOIN stock s ON s.ProductId = p.Id
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.Products p
JOIN master.ProductCategories c ON c.Id = p.CategoryId
{whereClause};";

        var args = new
        {
            f.Status,
            f.CategoryCode,
            f.Brand,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<ProductListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<ProductListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }

    public async Task<IReadOnlyDictionary<Guid, ProductLineMeta>> GetMetaByIdsAsync(
        IEnumerable<Guid> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, ProductLineMeta>();

        // Dapper expands the @ids parameter to (@p0, @p1, …) — works
        // up to ~2100 parameters, plenty for a single PO's line set.
        const string sql = @"
SELECT Id, Code, Name, TrackingMethod
FROM master.Products
WHERE Id IN @ids;";

        var rows = await _connection.QueryAsync<(Guid Id, string Code, string Name, string TrackingMethod)>(
            new CommandDefinition(sql, new { ids }, cancellationToken: ct));
        return rows.ToDictionary(
            r => r.Id,
            r => new ProductLineMeta(r.Code, r.Name, r.TrackingMethod));
    }
}
