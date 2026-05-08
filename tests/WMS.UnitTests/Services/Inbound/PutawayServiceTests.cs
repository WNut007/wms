using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Inbound;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.UnitTests.Services.Inbound;

public class PutawayServiceTests
{
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestProductId = Guid.NewGuid();
    private static readonly Guid TestOwnerId = Guid.NewGuid();
    private static readonly Guid TestUomId = Guid.NewGuid();
    private static readonly Guid FromLocId = Guid.NewGuid();
    private static readonly Guid ToLocId = Guid.NewGuid();

    [Fact]
    public async Task PutawayStockAsync_ZeroQuantity_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest(quantity: 0);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.PutawayStockAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task PutawayStockAsync_SameSourceAndDestination_Throws()
    {
        var sut = NewService(out _);
        var req = new PutawayRequest(
            FromKey: NewKey(FromLocId),
            ToLocationId: FromLocId,
            Quantity: 5);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.PutawayStockAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task PutawayStockAsync_NoSourceStockRow_Throws()
    {
        var sut = NewService(out var repo);
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<StockKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stock?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.PutawayStockAsync(TestTenantId, NewRequest(quantity: 5), currentUserId: null));
    }

    [Fact]
    public async Task PutawayStockAsync_HappyPath_DelegatesToTransfer()
    {
        var sut = NewService(out var repo);
        var sourceRow = NewStockRow(FromLocId, onHand: 25);
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<StockKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceRow);

        var afterSource = NewStockRow(FromLocId, onHand: 15);
        var dest = NewStockRow(ToLocId, onHand: 10);
        repo.Setup(r => r.TransferStockAsync(
                sourceRow.Id, ToLocId, 10m,
                It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((afterSource, dest));

        var result = await sut.PutawayStockAsync(
            TestTenantId, NewRequest(quantity: 10), currentUserId: null);

        Assert.Same(afterSource, result.Source);
        Assert.Same(dest, result.Destination);
        repo.Verify(r => r.TransferStockAsync(
            sourceRow.Id, ToLocId, 10m,
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PutawayStockAsync_PassesCurrentUserIdThrough()
    {
        var sut = NewService(out var repo);
        var userId = Guid.NewGuid();
        StockMovementContext? captured = null;

        var sourceRow = NewStockRow(FromLocId, onHand: 25);
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<StockKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceRow);
        repo.Setup(r => r.TransferStockAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, decimal, StockMovementContext, CancellationToken>(
                (_, _, _, ctx, _) => captured = ctx)
            .ReturnsAsync((sourceRow, sourceRow));

        await sut.PutawayStockAsync(TestTenantId, NewRequest(quantity: 1), userId);

        Assert.NotNull(captured);
        Assert.Equal(userId, captured!.PerformedBy);
    }

    private static PutawayService NewService(out Mock<IStockRepository> repo)
    {
        repo = new Mock<IStockRepository>();
        var factory = new Mock<IStockRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);
        return new PutawayService(factory.Object, NullLogger<PutawayService>.Instance);
    }

    private static StockKey NewKey(Guid locationId) =>
        new(locationId, TestProductId, LotId: null, PalletId: null, TestOwnerId, TestUomId);

    private static PutawayRequest NewRequest(decimal quantity) =>
        new(NewKey(FromLocId), ToLocId, quantity);

    private static Stock NewStockRow(Guid locationId, decimal onHand) => new()
    {
        Id = Guid.NewGuid(),
        LocationId = locationId,
        ProductId = TestProductId,
        OwnerId = TestOwnerId,
        UomId = TestUomId,
        QuantityOnHand = onHand,
        QuantityAllocated = 0,
        Version = 0,
    };
}
