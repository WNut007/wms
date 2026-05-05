using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Inbound;

namespace WMS.DAL.Repositories.Inbound;

// Dapper-backed PO repo. CreateAsync owns its transaction (open the
// connection here, BEGIN / COMMIT / ROLLBACK around the multi-row
// inserts) so the service stays free of ADO.NET plumbing. Reads use
// QueryMultiple to fetch header + lines in one round-trip.
internal sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private const string HeaderColumns = @"
        Id, PoNumber, OwnerId, WarehouseId, ExpectedDate,
        Status, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inbound.PurchaseOrders";

    private const string LineColumns = @"
        Id, PurchaseOrderId, LineNumber, ProductId, UomId,
        ExpectedQuantity, ReceivedQuantity, Status,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inbound.PurchaseOrderLines";

    private readonly IDbConnection _connection;

    public PurchaseOrderRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        PurchaseOrder header,
        IReadOnlyList<PurchaseOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default)
    {
        // Connection might be closed by the factory; open it before
        // beginning a transaction.
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        using var tx = _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO inbound.PurchaseOrders
                      (Id, PoNumber, OwnerId, WarehouseId, ExpectedDate,
                       Status, Notes, CreatedBy)
                  VALUES
                      (@Id, @PoNumber, @OwnerId, @WarehouseId, @ExpectedDate,
                       @Status, @Notes, @UserId);",
                new
                {
                    header.Id,
                    header.PoNumber,
                    header.OwnerId,
                    header.WarehouseId,
                    header.ExpectedDate,
                    header.Status,
                    header.Notes,
                    UserId = userId,
                },
                transaction: tx,
                cancellationToken: ct));

            // Dapper expands an IEnumerable parameter into a multi-row
            // INSERT under the hood, but multi-row INSERT doesn't accept
            // a list-of-records cleanly with named columns — issue one
            // INSERT per line. Line counts are operator-scale (typically
            // <50), so the round-trips don't dominate.
            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO inbound.PurchaseOrderLines
                          (Id, PurchaseOrderId, LineNumber, ProductId, UomId,
                           ExpectedQuantity, ReceivedQuantity, Status, CreatedBy)
                      VALUES
                          (@Id, @PurchaseOrderId, @LineNumber, @ProductId, @UomId,
                           @ExpectedQuantity, @ReceivedQuantity, @Status, @UserId);",
                    new
                    {
                        line.Id,
                        line.PurchaseOrderId,
                        line.LineNumber,
                        line.ProductId,
                        line.UomId,
                        line.ExpectedQuantity,
                        line.ReceivedQuantity,
                        line.Status,
                        UserId = userId,
                    },
                    transaction: tx,
                    cancellationToken: ct));
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public Task<PurchaseOrderDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE PurchaseOrderId = @id ORDER BY LineNumber;",
            new { id },
            ct);

    public Task<PurchaseOrderDetail?> GetByNumberAsync(string poNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            // Resolve the header by PoNumber, then match lines by the
            // header's Id — one round-trip via the QueryMultiple batch.
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM inbound.PurchaseOrders WHERE PoNumber = @poNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE PurchaseOrderId = @id ORDER BY LineNumber;",
            new { poNumber },
            ct);

    private async Task<PurchaseOrderDetail?> ReadDetailAsync(
        string sql,
        object args,
        CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<PurchaseOrder?>();
        if (header is null) return null;

        var lines = (await multi.ReadAsync<PurchaseOrderLine>()).AsList();
        return new PurchaseOrderDetail(header, lines);
    }

    public Task IncrementLineReceivedQuantityAsync(
        Guid poLineId,
        decimal delta,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE inbound.PurchaseOrderLines
              SET ReceivedQuantity = ReceivedQuantity + @Delta,
                  UpdatedAt        = SYSUTCDATETIME(),
                  UpdatedBy        = @UserId,
                  Version          = Version + 1
              WHERE Id = @PoLineId;",
            new
            {
                PoLineId = poLineId,
                Delta = delta,
                UserId = userId,
            },
            cancellationToken: ct));
}
