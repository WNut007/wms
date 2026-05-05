using System.Data;
using Dapper;

namespace WMS.DAL.Repositories.Inventory;

// Same MERGE WITH (HOLDLOCK) + trailing SELECT pattern as LotRepository
// — the unique index on PalletNumber serialises insert paths and the
// SELECT returns the row's Id whether we created it or it already
// existed.
internal sealed class PalletRepository : IPalletRepository
{
    private readonly IDbConnection _connection;

    public PalletRepository(IDbConnection connection) => _connection = connection;

    public Task<Guid> GetOrCreateAsync(
        string palletNumber,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.QuerySingleAsync<Guid>(new CommandDefinition(
            @"MERGE inventory.Pallets WITH (HOLDLOCK) AS target
              USING (SELECT @PalletNumber AS PalletNumber) AS src
              ON target.PalletNumber = src.PalletNumber
              WHEN NOT MATCHED THEN
                  INSERT (Id, PalletNumber, Status, CreatedAt, CreatedBy)
                  VALUES (NEWID(), src.PalletNumber, 'Active',
                          SYSUTCDATETIME(), @UserId);

              SELECT Id FROM inventory.Pallets
              WHERE PalletNumber = @PalletNumber;",
            new
            {
                PalletNumber = palletNumber,
                UserId = userId,
            },
            cancellationToken: ct));
}
