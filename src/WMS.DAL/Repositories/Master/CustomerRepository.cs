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
