using Microsoft.Extensions.Logging;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inbound;

// Receiving-α implementation: validate, build the 6-tuple key with
// LotId / PalletId pinned to NULL, delegate to the repo's atomic
// MERGE upsert, log the resulting Stock identity for traceability.
//
// Future chunks (Receiving-β onwards) will extend the request shape
// with optional Lot / Pallet identifiers — at that point the service
// upserts the Lot / Pallet rows first and feeds their Ids into the
// StockKey.
public sealed class ReceivingService : IReceivingService
{
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly ILogger<ReceivingService> _logger;

    public ReceivingService(
        IStockRepositoryFactory stockRepoFactory,
        ILogger<ReceivingService> logger)
    {
        _stockRepoFactory = stockRepoFactory;
        _logger = logger;
    }

    public async Task<Stock> ReceiveStockAsync(
        Guid tenantId,
        ReceiveStockRequest request,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            throw new ArgumentException(
                "Receive quantity must be positive.", nameof(request));

        var key = new StockKey(
            request.LocationId,
            request.ProductId,
            LotId: null,
            PalletId: null,
            request.OwnerId,
            request.UomId);

        var stock = await _stockRepoFactory.For(tenantId)
            .UpsertOnHandAsync(key, request.Quantity, currentUserId, ct);

        _logger.LogInformation(
            "Received {Qty} of product {ProductId} at location {LocationId} " +
            "(stock {StockId}, version {Version}, on-hand {OnHand})",
            request.Quantity, request.ProductId, request.LocationId,
            stock.Id, stock.Version, stock.QuantityOnHand);

        return stock;
    }
}
