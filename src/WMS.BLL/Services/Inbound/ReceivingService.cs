using Microsoft.Extensions.Logging;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inbound;

// Receive primitive. Resolves Lot and Pallet ids (via their per-tenant
// repos' atomic GetOrCreate) before delegating to the Stock repo's
// 6-tuple upsert. Lot and Pallet upserts run independently of each
// other and of the Stock upsert — each is its own MERGE and its own
// transaction. Orphan Lot / Pallet rows from a partial-failed receive
// are intentionally left in place; they're real warehouse identities
// that the operator has named, and will be reused on the next attempt.
public sealed class ReceivingService : IReceivingService
{
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly ILotRepositoryFactory _lotRepoFactory;
    private readonly IPalletRepositoryFactory _palletRepoFactory;
    private readonly ILogger<ReceivingService> _logger;

    public ReceivingService(
        IStockRepositoryFactory stockRepoFactory,
        ILotRepositoryFactory lotRepoFactory,
        IPalletRepositoryFactory palletRepoFactory,
        ILogger<ReceivingService> logger)
    {
        _stockRepoFactory = stockRepoFactory;
        _lotRepoFactory = lotRepoFactory;
        _palletRepoFactory = palletRepoFactory;
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

        Guid? lotId = null;
        if (request.Lot is { } lot)
        {
            lotId = await _lotRepoFactory.For(tenantId).GetOrCreateAsync(
                request.ProductId,
                lot.LotNumber,
                lot.ReceivedDate,
                lot.ExpiryDate,
                currentUserId,
                ct);
        }

        Guid? palletId = null;
        if (request.Pallet is { } pallet)
        {
            palletId = await _palletRepoFactory.For(tenantId).GetOrCreateAsync(
                pallet.PalletNumber,
                currentUserId,
                ct);
        }

        var key = new StockKey(
            request.LocationId,
            request.ProductId,
            lotId,
            palletId,
            request.OwnerId,
            request.UomId);

        // ReferenceType is non-null only when we have a ReceivingLine
        // to point at — direct receives (no header orchestration) leave
        // both null. Putaway-style "no reference" rows use 'Putaway' as
        // their ReferenceType + null Id; we follow the same shape: no
        // line means no provenance recorded, full stop.
        var movementCtx = new StockMovementContext(
            MovementType: StockMovementType.Receive,
            PerformedBy:  currentUserId,
            ReferenceType: request.ReceivingLineId is null ? null : "ReceivingLine",
            ReferenceId:   request.ReceivingLineId);

        var stock = await _stockRepoFactory.For(tenantId)
            .UpsertOnHandAsync(key, request.Quantity, movementCtx, ct);

        _logger.LogInformation(
            "Received {Qty} of product {ProductId} at location {LocationId} " +
            "(lot {LotId}, pallet {PalletId}, stock {StockId} v{Version}, on-hand {OnHand})",
            request.Quantity, request.ProductId, request.LocationId,
            lotId, palletId, stock.Id, stock.Version, stock.QuantityOnHand);

        return stock;
    }
}
