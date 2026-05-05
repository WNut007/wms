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
        var sut = NewService(out _, out _, out _);
        var req = NewRequest(quantity: 0);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ReceiveStockAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task ReceiveStockAsync_NegativeQuantity_Throws()
    {
        var sut = NewService(out _, out _, out _);
        var req = NewRequest(quantity: -1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ReceiveStockAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task ReceiveStockAsync_NoLotNoPallet_KeyHasNullLotAndPallet()
    {
        var sut = NewService(out var stockRepo, out var lotRepo, out var palletRepo);

        StockKey? captured = null;
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, Guid?, CancellationToken>(
                (k, _, _, _) => captured = k)
            .ReturnsAsync(NewStockRow(onHand: 5));

        await sut.ReceiveStockAsync(TestTenantId, NewRequest(quantity: 5), currentUserId: null);

        Assert.NotNull(captured);
        Assert.Null(captured!.LotId);
        Assert.Null(captured.PalletId);

        // Lot/Pallet repos must NOT be touched when neither is supplied.
        lotRepo.Verify(r => r.GetOrCreateAsync(
            It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        palletRepo.Verify(r => r.GetOrCreateAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReceiveStockAsync_WithLotOnly_UpsertsLotAndThreadsId()
    {
        var lotId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sut = NewService(out var stockRepo, out var lotRepo, out var palletRepo);

        lotRepo.Setup(r => r.GetOrCreateAsync(
                TestProductId, "LOT-A",
                It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(),
                userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lotId);

        StockKey? capturedKey = null;
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, Guid?, CancellationToken>(
                (k, _, _, _) => capturedKey = k)
            .ReturnsAsync(NewStockRow(onHand: 1));

        var req = NewRequest(quantity: 1) with
        {
            Lot = new LotInfo("LOT-A", new DateOnly(2026, 5, 6), new DateOnly(2027, 1, 1)),
        };
        await sut.ReceiveStockAsync(TestTenantId, req, userId);

        Assert.Equal(lotId, capturedKey!.LotId);
        Assert.Null(capturedKey.PalletId);
        palletRepo.Verify(r => r.GetOrCreateAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReceiveStockAsync_WithPalletOnly_UpsertsPalletAndThreadsId()
    {
        var palletId = Guid.NewGuid();
        var sut = NewService(out var stockRepo, out var lotRepo, out var palletRepo);

        palletRepo.Setup(r => r.GetOrCreateAsync(
                "PAL-001", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(palletId);

        StockKey? capturedKey = null;
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, Guid?, CancellationToken>(
                (k, _, _, _) => capturedKey = k)
            .ReturnsAsync(NewStockRow(onHand: 1));

        var req = NewRequest(quantity: 1) with { Pallet = new PalletInfo("PAL-001") };
        await sut.ReceiveStockAsync(TestTenantId, req, currentUserId: null);

        Assert.Null(capturedKey!.LotId);
        Assert.Equal(palletId, capturedKey.PalletId);
        lotRepo.Verify(r => r.GetOrCreateAsync(
            It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReceiveStockAsync_WithLotAndPallet_ThreadsBothIds()
    {
        var lotId = Guid.NewGuid();
        var palletId = Guid.NewGuid();
        var sut = NewService(out var stockRepo, out var lotRepo, out var palletRepo);

        lotRepo.Setup(r => r.GetOrCreateAsync(
                TestProductId, "LOT-X",
                It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lotId);
        palletRepo.Setup(r => r.GetOrCreateAsync(
                "PAL-X", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(palletId);

        StockKey? capturedKey = null;
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, Guid?, CancellationToken>(
                (k, _, _, _) => capturedKey = k)
            .ReturnsAsync(NewStockRow(onHand: 1));

        var req = NewRequest(quantity: 1) with
        {
            Lot = new LotInfo("LOT-X", new DateOnly(2026, 5, 6)),
            Pallet = new PalletInfo("PAL-X"),
        };
        await sut.ReceiveStockAsync(TestTenantId, req, currentUserId: null);

        Assert.Equal(lotId, capturedKey!.LotId);
        Assert.Equal(palletId, capturedKey.PalletId);
    }

    [Fact]
    public async Task ReceiveStockAsync_ReturnsStockRowFromRepo()
    {
        var sut = NewService(out var stockRepo, out _, out _);
        var expected = NewStockRow(onHand: 42);
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await sut.ReceiveStockAsync(
            TestTenantId, NewRequest(quantity: 42), currentUserId: null);

        Assert.Same(expected, result);
    }

    private static ReceivingService NewService(
        out Mock<IStockRepository> stockRepo,
        out Mock<ILotRepository> lotRepo,
        out Mock<IPalletRepository> palletRepo)
    {
        stockRepo = new Mock<IStockRepository>();
        lotRepo = new Mock<ILotRepository>();
        palletRepo = new Mock<IPalletRepository>();

        var stockFactory = new Mock<IStockRepositoryFactory>();
        stockFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(stockRepo.Object);

        var lotFactory = new Mock<ILotRepositoryFactory>();
        lotFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(lotRepo.Object);

        var palletFactory = new Mock<IPalletRepositoryFactory>();
        palletFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(palletRepo.Object);

        return new ReceivingService(
            stockFactory.Object,
            lotFactory.Object,
            palletFactory.Object,
            NullLogger<ReceivingService>.Instance);
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
