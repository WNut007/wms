using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Inbound;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.UnitTests.Services.Inbound;

public class ReceivingServiceTests
{
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestLocationId = Guid.NewGuid();
    private static readonly Guid TestProductId = Guid.NewGuid();
    private static readonly Guid TestOwnerId = Guid.NewGuid();
    private static readonly Guid TestUomId = Guid.NewGuid();

    [Fact]
    public async Task ReceiveStockAsync_ZeroQuantity_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest(quantity: 0);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ReceiveStockAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task ReceiveStockAsync_NegativeQuantity_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest(quantity: -1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ReceiveStockAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task ReceiveStockAsync_BuildsKey_WithNullLotAndPallet_AndForwardsToRepo()
    {
        var userId = Guid.NewGuid();
        var sut = NewService(out var repo);

        var captured = (StockKey?)null;
        repo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, Guid?, CancellationToken>(
                (k, _, _, _) => captured = k)
            .ReturnsAsync(NewStockRow(onHand: 5));

        await sut.ReceiveStockAsync(TestTenantId, NewRequest(quantity: 5), userId);

        Assert.NotNull(captured);
        Assert.Equal(TestLocationId, captured!.LocationId);
        Assert.Equal(TestProductId, captured.ProductId);
        Assert.Null(captured.LotId);
        Assert.Null(captured.PalletId);
        Assert.Equal(TestOwnerId, captured.OwnerId);
        Assert.Equal(TestUomId, captured.UomId);

        repo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), 5m, userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReceiveStockAsync_ResolvesRepoFromFactory_ForGivenTenant()
    {
        var factory = new Mock<IStockRepositoryFactory>(MockBehavior.Strict);
        var repo = new Mock<IStockRepository>();
        repo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewStockRow(onHand: 1));

        factory.Setup(f => f.For(TestTenantId)).Returns(repo.Object);

        var sut = new ReceivingService(factory.Object, NullLogger<ReceivingService>.Instance);
        await sut.ReceiveStockAsync(TestTenantId, NewRequest(quantity: 1), currentUserId: null);

        factory.Verify(f => f.For(TestTenantId), Times.Once);
    }

    [Fact]
    public async Task ReceiveStockAsync_ReturnsStockRowFromRepo()
    {
        var sut = NewService(out var repo);
        var expected = NewStockRow(onHand: 42);
        repo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await sut.ReceiveStockAsync(
            TestTenantId, NewRequest(quantity: 42), currentUserId: null);

        Assert.Same(expected, result);
    }

    private static ReceivingService NewService(out Mock<IStockRepository> repo)
    {
        repo = new Mock<IStockRepository>();
        var factory = new Mock<IStockRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        return new ReceivingService(factory.Object, NullLogger<ReceivingService>.Instance);
    }

    private static ReceiveStockRequest NewRequest(decimal quantity) =>
        new(TestLocationId, TestProductId, TestOwnerId, TestUomId, quantity);

    private static Stock NewStockRow(decimal onHand) => new()
    {
        Id = Guid.NewGuid(),
        LocationId = TestLocationId,
        ProductId = TestProductId,
        OwnerId = TestOwnerId,
        UomId = TestUomId,
        QuantityOnHand = onHand,
        QuantityAllocated = 0,
        Version = 0,
    };
}
