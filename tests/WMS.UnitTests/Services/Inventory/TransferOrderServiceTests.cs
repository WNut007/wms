using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Inventory;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.UnitTests.Services.Inventory;

// Phase 13 (ADR-012) — TransferOrderService unit tests. Cover
// validation, state-transition gating, separation-of-duties, and the
// happy paths through Dispatch/Receive that call into Stock.
public class TransferOrderServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid FromWh = Guid.NewGuid();
    private static readonly Guid ToWh = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid UomId = Guid.NewGuid();
    private static readonly Guid FromLoc = Guid.NewGuid();
    private static readonly Guid ToLoc = Guid.NewGuid();

    private static TransferOrderService NewService(
        out Mock<ITransferOrderRepository> repo,
        out Mock<IStockRepository> stockRepo)
    {
        repo = new Mock<ITransferOrderRepository>();
        var factory = new Mock<ITransferOrderRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        stockRepo = new Mock<IStockRepository>();
        var stockFactory = new Mock<IStockRepositoryFactory>();
        stockFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(stockRepo.Object);

        return new TransferOrderService(
            factory.Object,
            stockFactory.Object,
            NullLogger<TransferOrderService>.Instance);
    }

    private static CreateTransferOrderRequest NewRequest(
        Guid? from = null, Guid? to = null,
        string reason = "Rebalance",
        IReadOnlyList<CreateTransferOrderLineRequest>? lines = null) =>
        new(
            FromWarehouseId: from ?? FromWh,
            ToWarehouseId:   to   ?? ToWh,
            Reason: reason, Notes: null,
            Lines: lines ?? new List<CreateTransferOrderLineRequest> { NewLine(1) });

    private static CreateTransferOrderLineRequest NewLine(
        int n, decimal qty = 5m, Guid? fromLoc = null, Guid? toLoc = null) =>
        new(LineNumber: n, ProductId: ProductId, OwnerId: OwnerId, UomId: UomId,
            LotId: null, PalletId: null,
            FromLocationId: fromLoc ?? FromLoc, ToLocationId: toLoc ?? ToLoc,
            QtyRequested: qty, Notes: null);

    private static TransferOrder NewHeader(
        Guid id, string status = "Draft", Guid? requestedBy = null,
        string number = "TRN-X") => new()
    {
        Id = id,
        TransferNumber = number,
        FromWarehouseId = FromWh, ToWarehouseId = ToWh,
        Status = status,
        Reason = "Rebalance",
        RequestedBy = requestedBy ?? Guid.NewGuid(),
        RequestedAt = DateTime.UtcNow,
    };

    private static TransferOrderLine NewDomainLine(
        Guid lineId, Guid transferId, string status = "Pending",
        decimal requested = 5m, decimal? dispatched = null) => new()
    {
        Id = lineId, TransferId = transferId, LineNumber = 1,
        ProductId = ProductId, OwnerId = OwnerId, UomId = UomId,
        FromLocationId = FromLoc, ToLocationId = ToLoc,
        QtyRequested = requested, QtyDispatched = dispatched,
        Status = status,
    };

    // ================================================================
    // CreateAsync — validation
    // ================================================================

    [Fact]
    public async Task Create_FromEqualsTo_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(from: FromWh, to: FromWh), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_BlankReason_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(reason: ""), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_UnknownReason_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(reason: "NotARealReason"), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_EmptyUserId_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(), Guid.Empty));
    }

    [Fact]
    public async Task Create_NoLines_Throws()
    {
        var sut = NewService(out _, out _);
        var req = NewRequest(lines: new List<CreateTransferOrderLineRequest>());
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_DuplicateLineNumbers_Throws()
    {
        var sut = NewService(out _, out _);
        var req = NewRequest(lines: new List<CreateTransferOrderLineRequest>
        {
            NewLine(1), NewLine(1), // duplicate
        });
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_LineFromEqualsTo_Throws()
    {
        var sut = NewService(out _, out _);
        var req = NewRequest(lines: new List<CreateTransferOrderLineRequest>
        {
            NewLine(1, fromLoc: FromLoc, toLoc: FromLoc), // same loc
        });
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_HappyPath_AssignsNumberAndPersists()
    {
        var sut = NewService(out var repo, out _);
        var requesterId = Guid.NewGuid();

        repo.Setup(r => r.CountForDatePrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        TransferOrder? capturedHeader = null;
        IReadOnlyList<TransferOrderLine>? capturedLines = null;
        repo.Setup(r => r.CreateAsync(
                It.IsAny<TransferOrder>(),
                It.IsAny<IReadOnlyList<TransferOrderLine>>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<TransferOrder, IReadOnlyList<TransferOrderLine>, Guid, CancellationToken>(
                (h, l, _, _) => { capturedHeader = h; capturedLines = l; })
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new TransferOrderDetail(
                    capturedHeader ?? new TransferOrder { Id = id },
                    capturedLines ?? new List<TransferOrderLine>()));

        var saved = await sut.CreateAsync(TenantId, NewRequest(), requesterId);

        Assert.NotNull(capturedHeader);
        Assert.Equal("Draft", capturedHeader!.Status);
        Assert.Equal("Rebalance", capturedHeader.Reason);
        Assert.Equal(requesterId, capturedHeader.RequestedBy);
        var prefix = $"TRN-{DateTime.UtcNow:yyyyMMdd}-";
        Assert.StartsWith(prefix, capturedHeader.TransferNumber);
        Assert.EndsWith("0003", capturedHeader.TransferNumber);
        Assert.Single(capturedLines!);
        Assert.Equal("Pending", capturedLines![0].Status);
    }

    // ================================================================
    // SubmitAsync
    // ================================================================

    [Fact]
    public async Task Submit_AlreadySubmitted_ReturnsFalse()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Submitted"), new List<TransferOrderLine>()));

        var changed = await sut.SubmitAsync(TenantId, id, Guid.NewGuid());
        Assert.False(changed);
    }

    [Fact]
    public async Task Submit_FromApproved_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Approved"), new List<TransferOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SubmitAsync(TenantId, id, Guid.NewGuid()));
    }

    // ================================================================
    // ApproveAsync — separation of duties
    // ================================================================

    [Fact]
    public async Task Approve_SelfApproval_Throws()
    {
        var sut = NewService(out var repo, out _);
        var requesterId = Guid.NewGuid();
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Submitted", requesterId), new List<TransferOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApproveAsync(TenantId, id, requesterId));
    }

    [Fact]
    public async Task Approve_HappyPath_FlipsStatus()
    {
        var sut = NewService(out var repo, out _);
        var requesterId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Submitted", requesterId), new List<TransferOrderLine>()));
        repo.Setup(r => r.SetApprovedAsync(id, approverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.ApproveAsync(TenantId, id, approverId);

        Assert.True(ok);
        repo.Verify(r => r.SetApprovedAsync(id, approverId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // DispatchAsync — TX wrapping + Stock writes
    // ================================================================

    [Fact]
    public async Task Dispatch_EmptyLines_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.DispatchAsync(TenantId, Guid.NewGuid(),
                new List<DispatchLineRequest>(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Dispatch_WrongState_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Draft"), new List<TransferOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.DispatchAsync(TenantId, id,
                new List<DispatchLineRequest> { new(Guid.NewGuid(), 1m) }, Guid.NewGuid()));
    }

    [Fact]
    public async Task Dispatch_QtyExceedsRequested_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Approved"),
                new List<TransferOrderLine> { NewDomainLine(lineId, id, "Pending", requested: 5m) }));

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.DispatchAsync(TenantId, id,
                new List<DispatchLineRequest> { new(lineId, 6m) }, Guid.NewGuid()));
    }

    [Fact]
    public async Task Dispatch_HappyPath_WritesStock_FlipsHeader()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var dispatcherId = Guid.NewGuid();

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Approved"),
                new List<TransferOrderLine> { NewDomainLine(lineId, id, "Pending", requested: 5m) }));

        decimal capturedDelta = 0;
        StockMovementContext? capturedCtx = null;
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, StockMovementContext, CancellationToken>(
                (_, d, c, _) => { capturedDelta = d; capturedCtx = c; })
            .ReturnsAsync(new Stock { Id = Guid.NewGuid(), QuantityOnHand = 0 });

        repo.Setup(r => r.SetInTransitAsync(id, dispatcherId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.DispatchAsync(TenantId, id,
            new List<DispatchLineRequest> { new(lineId, 3m) }, dispatcherId);

        Assert.True(ok);
        Assert.Equal(-3m, capturedDelta); // negative — source decrement
        Assert.Equal(StockMovementType.Transfer, capturedCtx!.MovementType);
        Assert.Equal("TransferOrderLine", capturedCtx.ReferenceType);
        Assert.Equal(lineId, capturedCtx.ReferenceId);
        repo.Verify(r => r.UpdateLineDispatchedAsync(
            lineId, 3m, dispatcherId, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SetInTransitAsync(id, dispatcherId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // ReceiveAsync — variance vs full-receive status flip
    // ================================================================

    [Fact]
    public async Task Receive_QtyExceedsDispatched_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "InTransit"),
                new List<TransferOrderLine> { NewDomainLine(lineId, id, "Dispatched",
                    requested: 5m, dispatched: 3m) }));

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ReceiveAsync(TenantId, id,
                new List<ReceiveLineRequest> { new(lineId, 4m) }, Guid.NewGuid()));
    }

    [Fact]
    public async Task Receive_FullReceive_StatusReceived_StockIncremented()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "InTransit"),
                new List<TransferOrderLine> { NewDomainLine(lineId, id, "Dispatched",
                    requested: 5m, dispatched: 3m) }));

        decimal capturedDelta = 0;
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, StockMovementContext, CancellationToken>(
                (_, d, _, _) => capturedDelta = d)
            .ReturnsAsync(new Stock { Id = Guid.NewGuid(), QuantityOnHand = 3m });

        repo.Setup(r => r.SetReceivedAsync(id, receiverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.ReceiveAsync(TenantId, id,
            new List<ReceiveLineRequest> { new(lineId, 3m) }, receiverId);

        Assert.True(ok);
        Assert.Equal(3m, capturedDelta); // positive — destination increment
        repo.Verify(r => r.UpdateLineReceivedAsync(
            lineId, 3m, "Received", receiverId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Receive_ShortReceive_StatusVariance()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "InTransit"),
                new List<TransferOrderLine> { NewDomainLine(lineId, id, "Dispatched",
                    requested: 5m, dispatched: 3m) }));

        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Stock { Id = Guid.NewGuid(), QuantityOnHand = 2m });

        repo.Setup(r => r.SetReceivedAsync(id, receiverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.ReceiveAsync(TenantId, id,
            new List<ReceiveLineRequest> { new(lineId, 2m) }, receiverId);

        Assert.True(ok);
        repo.Verify(r => r.UpdateLineReceivedAsync(
            lineId, 2m, "Variance", receiverId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Receive_ZeroQty_NoStockWrite_StatusVariance()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "InTransit"),
                new List<TransferOrderLine> { NewDomainLine(lineId, id, "Dispatched",
                    requested: 5m, dispatched: 3m) }));

        repo.Setup(r => r.SetReceivedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var ok = await sut.ReceiveAsync(TenantId, id,
            new List<ReceiveLineRequest> { new(lineId, 0m) }, Guid.NewGuid());

        Assert.True(ok);
        // Zero-receive on a Dispatched line — full loss in transit on this
        // line. Stock NOT written (no inventory arrived).
        stockRepo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), It.IsAny<decimal>(),
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.UpdateLineReceivedAsync(
            lineId, 0m, "Variance", It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // CancelAsync
    // ================================================================

    [Fact]
    public async Task Cancel_BlankReason_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CancelAsync(TenantId, Guid.NewGuid(), "  ", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_FromInTransit_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "InTransit"), new List<TransferOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CancelAsync(TenantId, id, "test", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsFalse()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Cancelled"), new List<TransferOrderLine>()));

        var changed = await sut.CancelAsync(TenantId, id, "test", Guid.NewGuid());
        Assert.False(changed);
    }

    [Fact]
    public async Task Cancel_FromDraft_TriesEachPreInTransitState()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Approved"), new List<TransferOrderLine>()));

        // Simulate the chain — Draft + Submitted no-ops, Approved hits.
        repo.Setup(r => r.SetCancelledAsync(id, "Draft", "test", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.SetCancelledAsync(id, "Submitted", "test", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.SetCancelledAsync(id, "Approved", "test", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.CancelAsync(TenantId, id, "test", userId);

        Assert.True(ok);
        repo.Verify(r => r.SetCancelledAsync(
            id, "Approved", "test", userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ================================================================
    // MarkLostAsync
    // ================================================================

    [Fact]
    public async Task MarkLost_BlankReason_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.MarkLostAsync(TenantId, Guid.NewGuid(), " ", Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkLost_FromApproved_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "Approved"), new List<TransferOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.MarkLostAsync(TenantId, id, "stolen", Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkLost_HappyPath_FlipsStatus_NoStockWrite()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderDetail(
                NewHeader(id, "InTransit"), new List<TransferOrderLine>()));
        repo.Setup(r => r.SetLostAsync(id, "stolen", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.MarkLostAsync(TenantId, id, "stolen", userId);

        Assert.True(ok);
        // No stock writes — loss captured by QtyLossInTransit naturally.
        stockRepo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), It.IsAny<decimal>(),
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
