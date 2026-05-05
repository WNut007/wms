using Moq;
using WMS.BLL.Services.Inventory;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.UnitTests.Services.Inventory;

public class StockServiceTests
{
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestProductId = Guid.NewGuid();
    private static readonly Guid TestLocationId = Guid.NewGuid();
    private static readonly Guid TestOwnerId = Guid.NewGuid();
    private static readonly Guid TestUomId = Guid.NewGuid();

    [Fact]
    public async Task GetByKeyAsync_ResolvesRepoForTenant_AndForwardsKey()
    {
        var key = new StockKey(
            TestLocationId, TestProductId,
            LotId: null, PalletId: null,
            TestOwnerId, TestUomId);

        var repo = new Mock<IStockRepository>();
        repo.Setup(r => r.GetByKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stock?)null);

        var factory = new Mock<IStockRepositoryFactory>(MockBehavior.Strict);
        factory.Setup(f => f.For(TestTenantId)).Returns(repo.Object);

        var sut = new StockService(factory.Object);
        var result = await sut.GetByKeyAsync(TestTenantId, key);

        Assert.Null(result);
        factory.Verify(f => f.For(TestTenantId), Times.Once);
        repo.Verify(r => r.GetByKeyAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAvailableByProduct_KeepsOnlyRowsWithAvailableAboveZero()
    {
        var availableRow = NewStock(onHand: 10, allocated: 4);   // available 6
        var fullyAllocated = NewStock(onHand: 5,  allocated: 5); // available 0
        var emptyRow = NewStock(onHand: 0, allocated: 0);        // available 0

        var repo = new Mock<IStockRepository>();
        repo.Setup(r => r.GetByProductAsync(TestProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { availableRow, fullyAllocated, emptyRow });

        var factory = new Mock<IStockRepositoryFactory>();
        factory.Setup(f => f.For(TestTenantId)).Returns(repo.Object);

        var sut = new StockService(factory.Object);
        var result = await sut.GetAvailableByProductAsync(TestTenantId, TestProductId);

        Assert.Single(result);
        Assert.Same(availableRow, result[0]);
    }

    [Fact]
    public async Task GetByProductAsync_DelegatesToRepo()
    {
        var rows = new[] { NewStock(7, 0), NewStock(3, 1) };

        var repo = new Mock<IStockRepository>();
        repo.Setup(r => r.GetByProductAsync(TestProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var factory = new Mock<IStockRepositoryFactory>();
        factory.Setup(f => f.For(TestTenantId)).Returns(repo.Object);

        var sut = new StockService(factory.Object);
        var result = await sut.GetByProductAsync(TestTenantId, TestProductId);

        Assert.Equal(2, result.Count);
        repo.Verify(r => r.GetByProductAsync(TestProductId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Stock_QuantityAvailable_IsOnHandMinusAllocated()
    {
        var s = new Stock { QuantityOnHand = 10, QuantityAllocated = 3 };
        Assert.Equal(7, s.QuantityAvailable);
    }

    private static Stock NewStock(decimal onHand, decimal allocated) => new()
    {
        Id = Guid.NewGuid(),
        LocationId = TestLocationId,
        ProductId = TestProductId,
        OwnerId = TestOwnerId,
        UomId = TestUomId,
        QuantityOnHand = onHand,
        QuantityAllocated = allocated,
    };
}
