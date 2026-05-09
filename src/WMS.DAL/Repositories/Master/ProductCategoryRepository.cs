using System.Data;
using Dapper;
using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

internal sealed class ProductCategoryRepository : IProductCategoryRepository
{
    private readonly IDbConnection _connection;

    public ProductCategoryRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(CancellationToken ct = default)
    {
        // Path-ordered so the dropdown groups subtrees together.
        // NULL Path sinks to end via the CASE — categories without a
        // computed Path show up last rather than at the top.
        var rows = await _connection.QueryAsync<LookupItem>(new CommandDefinition(
            @"SELECT Id, Code, Name
              FROM master.ProductCategories
              WHERE IsActive = 1
              ORDER BY CASE WHEN Path IS NULL THEN 1 ELSE 0 END, Path, Name",
            cancellationToken: ct));
        return rows.AsList();
    }
}
