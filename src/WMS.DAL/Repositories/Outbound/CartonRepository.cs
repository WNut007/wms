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

    public Task<int> StampShipmentForSalesOrderAsync(
        Guid salesOrderId,
        Guid shipmentId,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE c
              SET c.ShipmentId = @ShipmentId,
                  c.UpdatedAt  = SYSUTCDATETIME(),
                  c.UpdatedBy  = @UserId
              FROM outbound.Cartons c
              JOIN outbound.PackTasks pt ON pt.Id = c.PackTaskId
              WHERE pt.SalesOrderId = @SalesOrderId
                AND c.ShipmentId IS NULL;",
            new { SalesOrderId = salesOrderId, ShipmentId = shipmentId, UserId = userId },
            cancellationToken: ct));

    public async Task<IReadOnlyList<Carton>> GetByShipmentIdAsync(
        Guid shipmentId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    Id, CartonNumber, PackTaskId, BoxTypeId, WeightKg, ShipmentId, Notes,
    CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
FROM outbound.Cartons
WHERE ShipmentId = @shipmentId
ORDER BY CartonNumber;";

        var rows = await _connection.QueryAsync<Carton>(new CommandDefinition(
            sql, new { shipmentId }, cancellationToken: ct));
        return rows.AsList();
    }
}
