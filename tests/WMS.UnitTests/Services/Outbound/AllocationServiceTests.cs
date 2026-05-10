using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.BLL.Strategies.Allocation;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Inventory;
using WMS.Domain.Entities.Outbound;

namespace WMS.UnitTests.Services.Outbound;

// Phase 14B — AllocationService unit tests. Service is TX-wrapped but
// tests don't verify the TX itself (integration-test concern in the
// TD-006 family) — they assert that all expected calls happened with
// the right arguments.
public class AllocationServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid UomId = Guid.NewGuid();

    private record Build(
        AllocationService Service,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IOrderAllocationRepository> AllocRepo,
        Mock<IStockRepository> StockRepo,
        Mock<IAllocationStrategy> Strategy);

    private static Build NewService()
    {
        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var allocRepo = new Mock<IOrderAllocationRepository>();
        var allocFactory = new Mock<IOrderAllocationRepositoryFactory>();
        allocFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(allocRepo.Object);

        var stockRepo = new Mock<IStockRepository>();
        var stockFactory = new Mock<IStockRepositoryFactory>();
        stockFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(stockRepo.Object);

        var strategy = new Mock<IAllocationStrategy>();
        strategy.SetupGet(s => s.Name).Returns("FIFO");
        var resolver = new Mock<IAllocationStrategyResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(strategy.Object);

        var service = new AllocationService(
            soFactory.Object,
            allocFactory.Object,
            stockFactory.Object,
            resolver.Object,
            NullLogger<AllocationService>.Instance);

        return new Build(service, soRepo, allocRepo, stockRepo, strategy);
    }

    private static SalesOrder NewHeader(Guid id, string status) => new()
    {
        Id = id,
        SoNumber = "SO-X",
        WarehouseId = WarehouseId,
        Status = status,
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static SalesOrderLine NewLine(
        Guid id, Guid soId, decimal ordered, decimal allocated = 0m) => new()
    {
        Id = id, SalesOrderId = soId, LineNumber = 1,
        ProductId = ProductId, OwnerId = OwnerId, UomId = UomId,
        OrderedQuantity = ordered,
        AllocatedQuantity = allocated,
    };

    // ================================================================
    // AllocateAsync — state gating
    // ================================================================

    [Fact]
    public async Task Allocate_WrongState_Draft_Throws()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewHeader(id, "Draft"), new List<SalesOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.AllocateAsync(TenantId, id, null, Guid.NewGuid()));
    }

    [Fact]
    public async Task Allocate_WrongState_Cancelled_Throws()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewHeader(id, "Cancelled"), new List<SalesOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.AllocateAsync(TenantId, id, null, Guid.NewGuid()));
    }

    [Fact]
    public async Task Allocate_AlreadyAllocated_NoOp_ReturnsResult()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Allocated"),
                new List<SalesOrderLine> { NewLine(lineId, id, 10m, allocated: 10m) }));

        var result = await b.Service.AllocateAsync(TenantId, id, null, Guid.NewGuid());

        Assert.True(result.IsFullyAllocated);
        b.AllocRepo.Verify(r => r.CreateAsync(
            It.IsAny<OrderAllocation>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ================================================================
    // AllocateAsync — happy paths
    // ================================================================

    [Fact]
    public async Task Allocate_FullFromOpen_FlipsToAllocated_WritesAllocation()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.SoRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Open"),
                new List<SalesOrderLine> { NewLine(lineId, id, ordered: 10m) }));

        var stock = new Stock { Id = stockId, QuantityOnHand = 50m, QuantityAllocated = 0m };
        b.StockRepo.Setup(r => r.GetAllocationCandidatesAsync(
                WarehouseId, ProductId, OwnerId, UomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Stock> { stock });

        b.Strategy.Setup(s => s.Allocate(It.IsAny<AllocationContext>()))
            .Returns((AllocationContext c) =>
                new AllocationDecision(
                    new[] { new AllocationPick(stockId, c.OutstandingQuantity) },
                    Shortfall: 0m));

        b.SoRepo.Setup(r => r.SetStatusAsync(
                id, "Open", "Allocated", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.AllocateAsync(TenantId, id, null, userId);

        Assert.True(result.IsFullyAllocated);
        Assert.Equal(1, result.LineCount);
        Assert.Equal(1, result.FullyAllocatedLineCount);
        b.AllocRepo.Verify(r => r.CreateAsync(
            It.Is<OrderAllocation>(a =>
                a.SalesOrderLineId == lineId
                && a.StockId == stockId
                && a.AllocatedQuantity == 10m
                && a.Status == "Active"),
            userId, It.IsAny<CancellationToken>()), Times.Once);
        b.StockRepo.Verify(r => r.AdjustQuantityAllocatedAsync(
            stockId, 10m, userId, It.IsAny<CancellationToken>()), Times.Once);
        b.SoRepo.Verify(r => r.AdjustLineAllocatedQuantityAsync(
            lineId, 10m, userId, It.IsAny<CancellationToken>()), Times.Once);
        b.SoRepo.Verify(r => r.SetStatusAsync(
            id, "Open", "Allocated", userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Allocate_PartialFromOpen_FlipsToAllocating_RecordsShortfall()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.SoRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Open"),
                new List<SalesOrderLine> { NewLine(lineId, id, ordered: 10m) }));

        b.StockRepo.Setup(r => r.GetAllocationCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Stock> { new() { Id = stockId, QuantityOnHand = 4m } });

        // Strategy returns partial: 4 picked, 6 short.
        b.Strategy.Setup(s => s.Allocate(It.IsAny<AllocationContext>()))
            .Returns(new AllocationDecision(
                new[] { new AllocationPick(stockId, 4m) },
                Shortfall: 6m));

        b.SoRepo.Setup(r => r.SetStatusAsync(
                id, "Open", "Allocating", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.AllocateAsync(TenantId, id, null, userId);

        Assert.False(result.IsFullyAllocated);
        Assert.Equal(0, result.FullyAllocatedLineCount);
        Assert.Equal(6m, result.ShortfallByLineId[lineId]);
        b.SoRepo.Verify(r => r.SetStatusAsync(
            id, "Open", "Allocating", userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Allocate_NoCandidates_NoWritesForLine_FullShortfall()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.SoRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Open"),
                new List<SalesOrderLine> { NewLine(lineId, id, ordered: 10m) }));

        b.StockRepo.Setup(r => r.GetAllocationCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Stock>());

        // Strategy with empty candidates → empty picks + full shortfall.
        b.Strategy.Setup(s => s.Allocate(It.IsAny<AllocationContext>()))
            .Returns(new AllocationDecision(Array.Empty<AllocationPick>(), 10m));

        b.SoRepo.Setup(r => r.SetStatusAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), "Allocating",
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.AllocateAsync(TenantId, id, null, userId);

        Assert.False(result.IsFullyAllocated);
        Assert.Equal(10m, result.ShortfallByLineId[lineId]);
        b.AllocRepo.Verify(r => r.CreateAsync(
            It.IsAny<OrderAllocation>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Allocate_LineAlreadyFullyAllocated_Skipped()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        var lineFull = Guid.NewGuid();
        var lineNew = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.SoRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Allocating"),
                new List<SalesOrderLine>
                {
                    NewLine(lineFull, id, ordered: 5m, allocated: 5m), // already full
                    NewLine(lineNew,  id, ordered: 5m, allocated: 0m), // outstanding 5
                }));

        b.StockRepo.Setup(r => r.GetAllocationCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Stock> { new() { Id = stockId, QuantityOnHand = 10m } });

        b.Strategy.Setup(s => s.Allocate(It.IsAny<AllocationContext>()))
            .Returns((AllocationContext c) => new AllocationDecision(
                new[] { new AllocationPick(stockId, c.OutstandingQuantity) }, 0m));

        b.SoRepo.Setup(r => r.SetStatusAsync(
                id, "Allocating", "Allocated", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.AllocateAsync(TenantId, id, null, userId);

        // Strategy called only ONCE — for the outstanding line.
        b.Strategy.Verify(s => s.Allocate(It.IsAny<AllocationContext>()), Times.Once);
        b.AllocRepo.Verify(r => r.CreateAsync(
            It.Is<OrderAllocation>(a => a.SalesOrderLineId == lineNew),
            userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ================================================================
    // ReleaseAllForSalesOrderAsync
    // ================================================================

    [Fact]
    public async Task ReleaseAll_BlankReason_Throws()
    {
        var b = NewService();
        await Assert.ThrowsAsync<ArgumentException>(
            () => b.Service.ReleaseAllForSalesOrderAsync(
                TenantId, Guid.NewGuid(), "  ", Guid.NewGuid()));
    }

    [Fact]
    public async Task ReleaseAll_NoActive_ReturnsZero_NoWrites()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        b.AllocRepo.Setup(r => r.GetActiveEntitiesBySalesOrderIdAsync(
                id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderAllocation>());

        var count = await b.Service.ReleaseAllForSalesOrderAsync(
            TenantId, id, "test", Guid.NewGuid());

        Assert.Equal(0, count);
        b.StockRepo.Verify(r => r.AdjustQuantityAllocatedAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReleaseAll_MultipleAllocations_DecrementsStockAndLine_AndBulkFlips()
    {
        var b = NewService();
        var id = Guid.NewGuid();
        var line1 = Guid.NewGuid();
        var line2 = Guid.NewGuid();
        var stock1 = Guid.NewGuid();
        var stock2 = Guid.NewGuid();
        var stock3 = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // line1 has TWO allocations summing to 8; line2 has ONE of 3.
        var entities = new List<OrderAllocation>
        {
            new() { Id = Guid.NewGuid(), SalesOrderLineId = line1, StockId = stock1, AllocatedQuantity = 5m, Status = "Active" },
            new() { Id = Guid.NewGuid(), SalesOrderLineId = line1, StockId = stock2, AllocatedQuantity = 3m, Status = "Active" },
            new() { Id = Guid.NewGuid(), SalesOrderLineId = line2, StockId = stock3, AllocatedQuantity = 3m, Status = "Active" },
        };
        b.AllocRepo.Setup(r => r.GetActiveEntitiesBySalesOrderIdAsync(
                id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);
        b.AllocRepo.Setup(r => r.ReleaseAllForSalesOrderAsync(
                id, "test", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var count = await b.Service.ReleaseAllForSalesOrderAsync(
            TenantId, id, "test", userId);

        Assert.Equal(3, count);
        // 3 stock decrements, one per allocation.
        b.StockRepo.Verify(r => r.AdjustQuantityAllocatedAsync(
            stock1, -5m, userId, It.IsAny<CancellationToken>()), Times.Once);
        b.StockRepo.Verify(r => r.AdjustQuantityAllocatedAsync(
            stock2, -3m, userId, It.IsAny<CancellationToken>()), Times.Once);
        b.StockRepo.Verify(r => r.AdjustQuantityAllocatedAsync(
            stock3, -3m, userId, It.IsAny<CancellationToken>()), Times.Once);
        // 2 line decrements, summed: line1 = -8, line2 = -3.
        b.SoRepo.Verify(r => r.AdjustLineAllocatedQuantityAsync(
            line1, -8m, userId, It.IsAny<CancellationToken>()), Times.Once);
        b.SoRepo.Verify(r => r.AdjustLineAllocatedQuantityAsync(
            line2, -3m, userId, It.IsAny<CancellationToken>()), Times.Once);
        // Bulk flip status.
        b.AllocRepo.Verify(r => r.ReleaseAllForSalesOrderAsync(
            id, "test", userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
