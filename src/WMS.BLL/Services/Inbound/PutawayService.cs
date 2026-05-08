using Microsoft.Extensions.Logging;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inbound;

// Validates the request, resolves the source Stock row by 6-tuple,
// then delegates to IStockRepository.TransferStockAsync — the repo's
// SQL batch is what makes the operation atomic. The pre-checks here
// surface clearer errors than the SQL THROW for the common mistakes
// (zero quantity, same source / destination location).
public sealed class PutawayService : IPutawayService
{
    private readonly IStockRepositoryFactory _stockRepoFactory;
    private readonly ILogger<PutawayService> _logger;

    public PutawayService(
        IStockRepositoryFactory stockRepoFactory,
        ILogger<PutawayService> logger)
    {
        _stockRepoFactory = stockRepoFactory;
        _logger = logger;
    }

    public async Task<PutawayResult> PutawayStockAsync(
        Guid tenantId,
        PutawayRequest request,
        Guid? currentUserId,
        CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            throw new ArgumentException(
                "Putaway quantity must be positive.", nameof(request));

        if (request.FromKey.LocationId == request.ToLocationId)
            throw new ArgumentException(
                "Destination location must differ from source.", nameof(request));

        var repo = _stockRepoFactory.For(tenantId);

        var source = await repo.GetByKeyAsync(request.FromKey, ct);
        if (source is null)
            throw new InvalidOperationException(
                "No stock row matches the source 6-tuple.");

        // ReferenceId is null because Putaway has no header/line table
        // yet — ADR-004 (hybrid putaway template + scoring) will
        // introduce one and TD-004 will close. ReferenceType='Putaway'
        // is still set so the rows are findable via a free-text scan
        // even without an Id.
        var movementCtx = new StockMovementContext(
            MovementType: StockMovementType.Putaway,
            PerformedBy:  currentUserId,
            ReferenceType: "Putaway",
            ReferenceId:   null);

        var (afterSource, destination) = await repo.TransferStockAsync(
            source.Id, request.ToLocationId, request.Quantity, movementCtx, ct);

        _logger.LogInformation(
            "Putaway {Qty} of product {ProductId} from {FromLocation} to {ToLocation} " +
            "(source {SourceId} OnHand {SourceOnHand} → destination {DestId} OnHand {DestOnHand})",
            request.Quantity, request.FromKey.ProductId,
            request.FromKey.LocationId, request.ToLocationId,
            afterSource.Id, afterSource.QuantityOnHand,
            destination.Id, destination.QuantityOnHand);

        return new PutawayResult(afterSource, destination);
    }
}
