using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Inbound;
using WMS.DAL.Repositories.Inbound;
using WMS.Domain.Entities.Inbound;
using WMS.Domain.Entities.Inventory;

namespace WMS.UnitTests.Services.Inbound;

public class ReceivingHeaderServiceTests
{
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestWarehouseId = Guid.NewGuid();
    private static readonly Guid TestProductId = Guid.NewGuid();
    private static readonly Guid TestUomId = Guid.NewGuid();
    private static readonly Guid TestOwnerId = Guid.NewGuid();
    private static readonly Guid TestLocationId = Guid.NewGuid();

    [Fact]
    public async Task PostReceivingAsync_BlankReceivingNumber_Throws()
    {
        var sut = NewService(out _, out _, out _);
        var req = NewRequest() with { ReceivingNumber = " " };

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.PostReceivingAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task PostReceivingAsync_NoLines_Throws()
    {
        var sut = NewService(out _, out _, out _);
        var req = NewRequest() with { Lines = Array.Empty<PostReceivingLineRequest>() };

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.PostReceivingAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task PostReceivingAsync_NonPositiveQuantity_Throws()
    {
        var sut = NewService(out _, out _, out _);
        var req = NewRequest() with
        {
            Lines = new[] { NewLine(1, qty: 0) },
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.PostReceivingAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task PostReceivingAsync_HappyPath_CreatesReceiveStocksAndUpdatesLineRefs()
    {
        var lotId = Guid.NewGuid();
        var palletId = Guid.NewGuid();

        var sut = NewService(out var receivingRepo, out var poRepo, out var receivingService);

        receivingRepo.Setup(r => r.CreateAsync(
                It.IsAny<ReceivingHeader>(),
                It.IsAny<IReadOnlyList<ReceivingLine>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        receivingService.Setup(s => s.ReceiveStockAsync(
                TestTenantId,
                It.IsAny<ReceiveStockRequest>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Stock
            {
                Id = Guid.NewGuid(),
                LotId = lotId,
                PalletId = palletId,
                QuantityOnHand = 5,
            });

        var headerStub = new ReceivingHeader { Id = Guid.NewGuid(), Status = "Posted" };
        var detailStub = new ReceivingDetail(headerStub, Array.Empty<ReceivingLine>());
        receivingRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailStub);

        var poLineId = Guid.NewGuid();
        var req = NewRequest() with
        {
            Lines = new[] { NewLine(1, qty: 5, poLineId: poLineId) },
        };

        var result = await sut.PostReceivingAsync(TestTenantId, req, currentUserId: null);

        Assert.Same(detailStub, result);

        // ReceiveStockAsync called once, with the line's data
        receivingService.Verify(s => s.ReceiveStockAsync(
            TestTenantId,
            It.Is<ReceiveStockRequest>(r =>
                r.Quantity == 5 && r.ProductId == TestProductId),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Lot/Pallet stamped back on the receiving line
        receivingRepo.Verify(r => r.UpdateLineInventoryRefsAsync(
            It.IsAny<Guid>(), lotId, palletId,
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // PO line bumped exactly once with the received quantity
        poRepo.Verify(r => r.IncrementLineReceivedQuantityAsync(
            poLineId, 5m, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostReceivingAsync_NoPoLineLink_SkipsPoBump()
    {
        var sut = NewService(out var receivingRepo, out var poRepo, out var receivingService);

        receivingService.Setup(s => s.ReceiveStockAsync(
                It.IsAny<Guid>(), It.IsAny<ReceiveStockRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Stock { Id = Guid.NewGuid(), QuantityOnHand = 1 });

        receivingRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceivingDetail(
                new ReceivingHeader { Id = Guid.NewGuid() },
                Array.Empty<ReceivingLine>()));

        // poLineId = null
        var req = NewRequest() with
        {
            Lines = new[] { NewLine(1, qty: 1, poLineId: null) },
        };

        await sut.PostReceivingAsync(TestTenantId, req, currentUserId: null);

        poRepo.Verify(r => r.IncrementLineReceivedQuantityAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostReceivingAsync_MultipleLines_BumpsEachLinkedPoLine()
    {
        var sut = NewService(out var receivingRepo, out var poRepo, out var receivingService);

        receivingService.Setup(s => s.ReceiveStockAsync(
                It.IsAny<Guid>(), It.IsAny<ReceiveStockRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Stock { Id = Guid.NewGuid(), QuantityOnHand = 1 });

        receivingRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceivingDetail(
                new ReceivingHeader { Id = Guid.NewGuid() },
                Array.Empty<ReceivingLine>()));

        var poLine1 = Guid.NewGuid();
        var poLine2 = Guid.NewGuid();
        var req = NewRequest() with
        {
            Lines = new[]
            {
                NewLine(1, qty: 3, poLineId: poLine1),
                NewLine(2, qty: 7, poLineId: poLine2),
                NewLine(3, qty: 4, poLineId: null),  // blind
            },
        };

        await sut.PostReceivingAsync(TestTenantId, req, currentUserId: null);

        poRepo.Verify(r => r.IncrementLineReceivedQuantityAsync(
            poLine1, 3m, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        poRepo.Verify(r => r.IncrementLineReceivedQuantityAsync(
            poLine2, 7m, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        poRepo.Verify(r => r.IncrementLineReceivedQuantityAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static ReceivingHeaderService NewService(
        out Mock<IReceivingHeaderRepository> receivingRepo,
        out Mock<IPurchaseOrderRepository> poRepo,
        out Mock<IReceivingService> receivingService)
    {
        receivingRepo = new Mock<IReceivingHeaderRepository>();
        poRepo = new Mock<IPurchaseOrderRepository>();
        receivingService = new Mock<IReceivingService>();

        var receivingFactory = new Mock<IReceivingHeaderRepositoryFactory>();
        receivingFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(receivingRepo.Object);

        var poFactory = new Mock<IPurchaseOrderRepositoryFactory>();
        poFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(poRepo.Object);

        var poService = new Mock<IPurchaseOrderService>();
        // Phase 9B — service now calls Mark{Receiving,Closed}Async on
        // PO after each posted (non-Draft) receipt. Mock returns false
        // (no-op transition) so existing tests don't have to set up
        // verification on those calls.
        poService.Setup(s => s.MarkReceivingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        poService.Setup(s => s.MarkClosedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return new ReceivingHeaderService(
            receivingFactory.Object,
            poFactory.Object,
            receivingService.Object,
            poService.Object,
            NullLogger<ReceivingHeaderService>.Instance);
    }

    private static PostReceivingRequest NewRequest() =>
        new(
            ReceivingNumber: "REC-0001",
            PurchaseOrderId: null,
            WarehouseId: TestWarehouseId,
            ReceivedAt: null,
            Notes: null,
            Lines: new[] { NewLine(1, qty: 1) });

    private static PostReceivingLineRequest NewLine(
        int lineNumber, decimal qty, Guid? poLineId = null) =>
        new(
            LineNumber: lineNumber,
            PurchaseOrderLineId: poLineId,
            ProductId: TestProductId,
            UomId: TestUomId,
            OwnerId: TestOwnerId,
            LocationId: TestLocationId,
            ReceivedQuantity: qty);
}
