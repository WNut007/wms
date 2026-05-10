using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Dapper-backed reader for master.Customers. Bound to a single tenant
// DB connection in its ctor; the factory creates one per tenantId via
// ITenantConnectionFactory. Mirrors ProductRepository's shape minus the
// JOIN aggregate — Customer list reads master.Customers directly.
//
// SELECT lists omit Version — master.Customers has no Version column
// (BaseEntity.Version stays at default 0).
internal sealed class CustomerRepository : ICustomerRepository
{
    private const string EntitySelect = @"
        SELECT Id, Code, Name, CustomerType, CompanyName, TaxId,
               Email, Phone, CustomerTier, AnnualRevenue,
               OrdersPerMonth, AvgOrderValue, IsKeyAccount, IsStrategic,
               AllocationPriority, SafetyStockDays, PromisedFillRate,
               PreferredCarrierId, DefaultPaymentTerms, Status, Country,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM master.Customers";

    private readonly IDbConnection _connection;

    public CustomerRepository(IDbConnection connection) => _connection = connection;

    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Customer?>(new CommandDefinition(
            EntitySelect + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Customer?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Customer?>(new CommandDefinition(
            EntitySelect + " WHERE Code = @code",
            new { code },
            cancellationToken: ct));

    public async Task<Guid> InsertAsync(
        Customer entity, Guid? userId, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        // CreatedAt set server-side via SYSUTCDATETIME(). UpdatedAt
        // left NULL on insert. No Version column on master.Customers.
        const string sql = @"
INSERT INTO master.Customers
    (Id, Code, Name, CustomerType, CompanyName, TaxId,
     Email, Phone, CustomerTier, AnnualRevenue,
     OrdersPerMonth, AvgOrderValue, IsKeyAccount, IsStrategic,
     AllocationPriority, SafetyStockDays, PromisedFillRate,
     PreferredCarrierId, DefaultPaymentTerms, Status, Country,
     CreatedAt, CreatedBy)
VALUES
    (@Id, @Code, @Name, @CustomerType, @CompanyName, @TaxId,
     @Email, @Phone, @CustomerTier, @AnnualRevenue,
     @OrdersPerMonth, @AvgOrderValue, @IsKeyAccount, @IsStrategic,
     @AllocationPriority, @SafetyStockDays, @PromisedFillRate,
     @PreferredCarrierId, @DefaultPaymentTerms, @Status, @Country,
     SYSUTCDATETIME(), @UserId);";

        await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Code,
                entity.Name,
                entity.CustomerType,
                entity.CompanyName,
                entity.TaxId,
                entity.Email,
                entity.Phone,
                entity.CustomerTier,
                entity.AnnualRevenue,
                entity.OrdersPerMonth,
                entity.AvgOrderValue,
                entity.IsKeyAccount,
                entity.IsStrategic,
                entity.AllocationPriority,
                entity.SafetyStockDays,
                entity.PromisedFillRate,
                entity.PreferredCarrierId,
                entity.DefaultPaymentTerms,
                entity.Status,
                entity.Country,
                UserId = userId,
            },
            cancellationToken: ct));
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(
        Customer entity, Guid? userId, CancellationToken ct = default)
    {
        // Code + CustomerType NOT in SET — Code is read-only (FK orphan),
        // CustomerType flip orphans B2B-only fields. CompanyName/TaxId
        // legitimately update (e.g. B2B customer renaming).
        const string sql = @"
UPDATE master.Customers SET
    Name                = @Name,
    CompanyName         = @CompanyName,
    TaxId               = @TaxId,
    Email               = @Email,
    Phone               = @Phone,
    CustomerTier        = @CustomerTier,
    AnnualRevenue       = @AnnualRevenue,
    OrdersPerMonth      = @OrdersPerMonth,
    AvgOrderValue       = @AvgOrderValue,
    IsKeyAccount        = @IsKeyAccount,
    IsStrategic         = @IsStrategic,
    AllocationPriority  = @AllocationPriority,
    SafetyStockDays     = @SafetyStockDays,
    PromisedFillRate    = @PromisedFillRate,
    PreferredCarrierId  = @PreferredCarrierId,
    DefaultPaymentTerms = @DefaultPaymentTerms,
    Status              = @Status,
    Country             = @Country,
    UpdatedAt           = SYSUTCDATETIME(),
    UpdatedBy           = @UserId
WHERE Id = @Id;";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Name,
                entity.CompanyName,
                entity.TaxId,
                entity.Email,
                entity.Phone,
                entity.CustomerTier,
                entity.AnnualRevenue,
                entity.OrdersPerMonth,
                entity.AvgOrderValue,
                entity.IsKeyAccount,
                entity.IsStrategic,
                entity.AllocationPriority,
                entity.SafetyStockDays,
                entity.PromisedFillRate,
                entity.PreferredCarrierId,
                entity.DefaultPaymentTerms,
                entity.Status,
                entity.Country,
                UserId = userId,
            },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // Phase 14A — populates the Customer dropdown on SO Create.
        // Active-only; sorted by Code for predictable picker ordering.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.Customers
              WHERE Status = 'Active'
              ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PagedResult<CustomerListRow>> GetPagedAsync(
        CustomerFilter f, CancellationToken ct = default)
    {
        var orderBy = CustomerSortMapper.ToOrderByClause(f.SortBy, f.SortDesc);
        var skip    = (f.Page - 1) * f.PageSize;
        var take    = f.PageSize;
        var searchLike = string.IsNullOrWhiteSpace(f.Search)
            ? null
            : $"%{f.Search.Trim()}%";

        // Same WHERE used by page query and COUNT — single source of
        // truth so they can never drift.
        const string whereClause = @"
WHERE (@Status IS NULL OR Status = @Status)
  AND (@CustomerType IS NULL OR CustomerType = @CustomerType)
  AND (@Country IS NULL OR Country = @Country)
  AND (@SearchLike IS NULL
       OR Code  LIKE @SearchLike
       OR Name  LIKE @SearchLike
       OR Email LIKE @SearchLike)";

        var sql = $@"
SELECT
    Id, Code, Name, CustomerType,
    Country, Email, Status, UpdatedAt
FROM master.Customers
{whereClause}
ORDER BY {orderBy}
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

SELECT COUNT(*)
FROM master.Customers
{whereClause};";

        var args = new
        {
            f.Status,
            f.CustomerType,
            f.Country,
            SearchLike = searchLike,
            Skip = skip,
            Take = take,
        };

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<CustomerListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new PagedResult<CustomerListRow>
        {
            Items = items,
            Total = total,
            Page = f.Page,
            PageSize = f.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)f.PageSize),
        };
    }
}
