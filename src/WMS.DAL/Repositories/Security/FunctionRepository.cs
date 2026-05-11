using System.Data;
using Dapper;
using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

internal sealed class FunctionRepository : IFunctionRepository
{
    private const string SelectColumns = @"
        SELECT Id, Code, Name, Module, DisplayOrder, IsActive, CreatedAt
        FROM security.Functions";

    private readonly IDbConnection _connection;

    public FunctionRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<Function>> GetAllActiveAsync(CancellationToken ct = default)
    {
        var rows = await _connection.QueryAsync<Function>(new CommandDefinition(
            SelectColumns + " WHERE IsActive = 1 ORDER BY Module, DisplayOrder, Code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task<Function?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Function?>(new CommandDefinition(
            SelectColumns + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));
}
