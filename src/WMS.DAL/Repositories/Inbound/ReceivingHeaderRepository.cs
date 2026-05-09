using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Inbound;

namespace WMS.DAL.Repositories.Inbound;

// Same shape as PurchaseOrderRepository — Create owns its transaction;
// reads use Dapper QueryMultiple to fetch header + lines in one
// round-trip.
internal sealed class ReceivingHeaderRepository : IReceivingHeaderRepository
{
    private const string HeaderColumns = @"
        Id, ReceivingNumber, PurchaseOrderId, WarehouseId, ReceivedAt,
        Status, Notes,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inbound.ReceivingHeaders";

    private const string LineColumns = @"
        Id, ReceivingHeaderId, LineNumber, PurchaseOrderLineId,
        ProductId, UomId, OwnerId, LocationId,
        ReceivedQuantity, LotNumber, PalletNumber, LotId, PalletId,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inbound.ReceivingLines";

    private readonly IDbConnection _connection;

    public ReceivingHeaderRepository(IDbConnection connection) => _connection = connection;

    public async Task CreateAsync(
        ReceivingHeader header,
        IReadOnlyList<ReceivingLine> lines,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as SqlConnection)?.Open();

        using var tx = _connection.BeginTransaction();
        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO inbound.ReceivingHeaders
                      (Id, ReceivingNumber, PurchaseOrderId, WarehouseId,
                       ReceivedAt, Status, Notes, CreatedBy)
                  VALUES
                      (@Id, @ReceivingNumber, @PurchaseOrderId, @WarehouseId,
                       @ReceivedAt, @Status, @Notes, @UserId);",
                new
                {
                    header.Id,
                    header.ReceivingNumber,
                    header.PurchaseOrderId,
                    header.WarehouseId,
                    header.ReceivedAt,
                    header.Status,
                    header.Notes,
                    UserId = userId,
                },
                transaction: tx,
                cancellationToken: ct));

            // Per-line INSERT — same rationale as PurchaseOrderRepository
            // (Dapper's enumerable expansion fights named-column inserts;
            // line counts are operator-scale).
            foreach (var line in lines)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO inbound.ReceivingLines
                          (Id, ReceivingHeaderId, LineNumber, PurchaseOrderLineId,
                           ProductId, UomId, OwnerId, LocationId,
                           ReceivedQuantity, LotNumber, PalletNumber,
                           LotId, PalletId, CreatedBy)
                      VALUES
                          (@Id, @ReceivingHeaderId, @LineNumber, @PurchaseOrderLineId,
                           @ProductId, @UomId, @OwnerId, @LocationId,
                           @ReceivedQuantity, @LotNumber, @PalletNumber,
                           @LotId, @PalletId, @UserId);",
                    new
                    {
                        line.Id,
                        line.ReceivingHeaderId,
                        line.LineNumber,
                        line.PurchaseOrderLineId,
                        line.ProductId,
                        line.UomId,
                        line.OwnerId,
                        line.LocationId,
                        line.ReceivedQuantity,
                        line.LotNumber,
                        line.PalletNumber,
                        line.LotId,
                        line.PalletId,
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

    public Task<ReceivingDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE ReceivingHeaderId = @id ORDER BY LineNumber;",
            new { id },
            ct);

    public Task<ReceivingDetail?> GetByNumberAsync(string receivingNumber, CancellationToken ct = default) =>
        ReadDetailAsync(
            @"DECLARE @id UNIQUEIDENTIFIER =
                  (SELECT Id FROM inbound.ReceivingHeaders WHERE ReceivingNumber = @receivingNumber);
              SELECT " + HeaderColumns + @" WHERE Id = @id;
              SELECT " + LineColumns + @" WHERE ReceivingHeaderId = @id ORDER BY LineNumber;",
            new { receivingNumber },
            ct);

    public Task UpdateLineInventoryRefsAsync(
        Guid lineId,
        Guid? lotId,
        Guid? palletId,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE inbound.ReceivingLines
              SET LotId      = @LotId,
                  PalletId   = @PalletId,
                  UpdatedAt  = SYSUTCDATETIME(),
                  UpdatedBy  = @UserId,
                  Version    = Version + 1
              WHERE Id = @LineId;",
            new
            {
                LineId = lineId,
                LotId = lotId,
                PalletId = palletId,
                UserId = userId,
            },
            cancellationToken: ct));

    private async Task<ReceivingDetail?> ReadDetailAsync(
        string sql,
        object args,
        CancellationToken ct)
    {
        using var multi = await _connection.QueryMultipleAsync(
            new CommandDefinition(sql, args, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<ReceivingHeader?>();
        if (header is null) return null;

        var lines = (await multi.ReadAsync<ReceivingLine>()).AsList();
        return new ReceivingDetail(header, lines);
    }

    public async Task<IReadOnlyList<ReceivingActivityRow>> GetActivityByPoAsync(
        Guid purchaseOrderId, int limit = 20, CancellationToken ct = default)
    {
        // IX_ReceivingHeaders_PurchaseOrder covers the WHERE; no
        // dedicated index on (PoId, ReceivedAt) but at typical volumes
        // (a PO has <20 receipts in its lifecycle) the sort is cheap.
        var rows = await _connection.QueryAsync<ReceivingActivityRow>(new CommandDefinition(
            @"SELECT
                  h.Id,
                  h.ReceivingNumber,
                  h.ReceivedAt,
                  h.Status,
                  COALESCE(u.FullName, u.Email, 'System') AS PerformedByName,
                  (SELECT COUNT(*) FROM inbound.ReceivingLines rl
                   WHERE rl.ReceivingHeaderId = h.Id) AS LineCount
              FROM inbound.ReceivingHeaders h
              LEFT JOIN security.Users u ON u.Id = h.CreatedBy
              WHERE h.PurchaseOrderId = @purchaseOrderId
              ORDER BY h.ReceivedAt DESC
              OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            new { purchaseOrderId, limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ReceivingActivityRow>> GetActivityByWarehouseAsync(
        Guid warehouseId, int limit = 20, CancellationToken ct = default)
    {
        // IX_ReceivingHeaders_Warehouse(WarehouseId, ReceivedAt DESC)
        // covers the WHERE + ORDER exactly. The line-count subquery is
        // a correlated SELECT COUNT(*) — at <=20 rows that's a row-by-
        // row index lookup against ReceivingLines.ReceivingHeaderId,
        // which is the line table's leading FK index. Cheap.
        // COALESCE handles NULL PerformedBy (system-imported headers)
        // → falls back to literal 'System', same convention as
        // StockMovementRepository.GetByProductAsync.
        var rows = await _connection.QueryAsync<ReceivingActivityRow>(new CommandDefinition(
            @"SELECT
                  h.Id,
                  h.ReceivingNumber,
                  h.ReceivedAt,
                  h.Status,
                  COALESCE(u.FullName, u.Email, 'System') AS PerformedByName,
                  (SELECT COUNT(*) FROM inbound.ReceivingLines rl
                   WHERE rl.ReceivingHeaderId = h.Id) AS LineCount
              FROM inbound.ReceivingHeaders h
              LEFT JOIN security.Users u ON u.Id = h.CreatedBy
              WHERE h.WarehouseId = @warehouseId
              ORDER BY h.ReceivedAt DESC
              OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY",
            new { warehouseId, limit },
            cancellationToken: ct));
        return rows.AsList();
    }
}
