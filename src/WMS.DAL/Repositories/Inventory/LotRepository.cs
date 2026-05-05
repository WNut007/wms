using System.Data;
using Dapper;

namespace WMS.DAL.Repositories.Inventory;

// MERGE WITH (HOLDLOCK) on the UX_Lots_Product_Number index range
// serialises concurrent inserts of the same (ProductId, LotNumber).
// Insert path runs once; every other concurrent caller skips
// straight to the trailing SELECT, which returns the same Id.
//
// Two-statement batch rather than OUTPUT-into because MERGE's OUTPUT
// only fires for executed actions — without a (wasteful) self-update
// in WHEN MATCHED, the update path would emit nothing. Following the
// MERGE with a SELECT avoids the wasted write while keeping both
// branches in one round-trip.
internal sealed class LotRepository : ILotRepository
{
    private readonly IDbConnection _connection;

    public LotRepository(IDbConnection connection) => _connection = connection;

    public Task<Guid> GetOrCreateAsync(
        Guid productId,
        string lotNumber,
        DateOnly receivedDate,
        DateOnly? expiryDate,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.QuerySingleAsync<Guid>(new CommandDefinition(
            @"MERGE inventory.Lots WITH (HOLDLOCK) AS target
              USING (
                  SELECT @ProductId AS ProductId,
                         @LotNumber AS LotNumber
              ) AS src
              ON  target.ProductId = src.ProductId
              AND target.LotNumber = src.LotNumber
              WHEN NOT MATCHED THEN
                  INSERT (Id, ProductId, LotNumber, ReceivedDate, ExpiryDate,
                          Status, CreatedAt, CreatedBy)
                  VALUES (NEWID(), src.ProductId, src.LotNumber,
                          @ReceivedDate, @ExpiryDate,
                          'Active', SYSUTCDATETIME(), @UserId);

              SELECT Id FROM inventory.Lots
              WHERE ProductId = @ProductId AND LotNumber = @LotNumber;",
            new
            {
                ProductId = productId,
                LotNumber = lotNumber,
                ReceivedDate = receivedDate,
                ExpiryDate = expiryDate,
                UserId = userId,
            },
            cancellationToken: ct));
}
