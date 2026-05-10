using System.Data;
using Dapper;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

internal sealed class CartonRepository : ICartonRepository
{
    private readonly IDbConnection _connection;

    public CartonRepository(IDbConnection connection) => _connection = connection;

    public Task CreateAsync(
        Carton c, Guid? userId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO outbound.Cartons
                  (Id, CartonNumber, PackTaskId, BoxTypeId, WeightKg, Notes,
                   CreatedBy)
              VALUES
                  (@Id, @CartonNumber, @PackTaskId, @BoxTypeId, @WeightKg, @Notes,
                   @UserId);",
            new
            {
                c.Id, c.CartonNumber, c.PackTaskId,
                c.BoxTypeId, c.WeightKg, c.Notes,
                UserId = userId,
            },
            cancellationToken: ct));

    public Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default) =>
        _connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM outbound.Cartons
              WHERE CartonNumber LIKE @prefix + '%';",
            new { prefix = datePrefix },
            cancellationToken: ct));
}
