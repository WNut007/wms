using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Inbound;
using WMS.DAL.Repositories.Inbound;
using WMS.Domain.Entities.Inbound;

namespace WMS.UnitTests.Services.Inbound;

public class PurchaseOrderServiceTests
{
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestOwnerId = Guid.NewGuid();
    private static readonly Guid TestWarehouseId = Guid.NewGuid();
    private static readonly Guid TestProductId = Guid.NewGuid();
    private static readonly Guid TestUomId = Guid.NewGuid();

    [Fact]
    public async Task CreateAsync_BlankPoNumber_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest() with { PoNumber = "  " };

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task CreateAsync_EmptyOwnerId_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest() with { OwnerId = Guid.Empty };

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task CreateAsync_NoLines_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest() with { Lines = Array.Empty<CreatePurchaseOrderLineRequest>() };

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task CreateAsync_DuplicateLineNumbers_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest() with
        {
            Lines = new[]
            {
                new CreatePurchaseOrderLineRequest(1, TestProductId, TestUomId, 5),
                new CreatePurchaseOrderLineRequest(1, TestProductId, TestUomId, 3),
            },
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task CreateAsync_NonPositiveExpectedQuantity_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest() with
        {
            Lines = new[]
            {
                new CreatePurchaseOrderLineRequest(1, TestProductId, TestUomId, 0),
            },
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TestTenantId, req, currentUserId: null));
    }

    [Fact]
    public async Task CreateAsync_HappyPath_DelegatesToRepoAndReturnsDetail()
    {
        var sut = NewService(out var repo);
        var detail = NewDetailStub();

        repo.Setup(r => r.CreateAsync(
                It.IsAny<PurchaseOrder>(),
                It.IsAny<IReadOnlyList<PurchaseOrderLine>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await sut.CreateAsync(TestTenantId, NewRequest(), currentUserId: null);

        Assert.Same(detail, result);
        repo.Verify(r => r.CreateAsync(
            It.IsAny<PurchaseOrder>(),
            It.IsAny<IReadOnlyList<PurchaseOrderLine>>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_PassesCurrentUserIdToRepo()
    {
        var sut = NewService(out var repo);
        var userId = Guid.NewGuid();

        Guid? capturedUserId = Guid.NewGuid();  // any non-null sentinel
        repo.Setup(r => r.CreateAsync(
                It.IsAny<PurchaseOrder>(),
                It.IsAny<IReadOnlyList<PurchaseOrderLine>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<PurchaseOrder, IReadOnlyList<PurchaseOrderLine>, Guid?, CancellationToken>(
                (_, _, u, _) => capturedUserId = u)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewDetailStub());

        await sut.CreateAsync(TestTenantId, NewRequest(), userId);

        Assert.Equal(userId, capturedUserId);
    }

    [Fact]
    public async Task GetByIdAsync_DelegatesToRepo()
    {
        var sut = NewService(out var repo);
        var detail = NewDetailStub();
        var poId = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await sut.GetByIdAsync(TestTenantId, poId);

        Assert.Same(detail, result);
    }

    // ==================================================================
    // d.2.3.a — UpdatePartialAsync (surgical partial update)
    // ==================================================================

    [Fact]
    public async Task UpdatePartialAsync_PoNotFound_Throws()
    {
        var sut = NewService(out var repo);
        var poId = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderDetail?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdatePartialAsync(TestTenantId, poId, EmptyPartialRequest(), null));
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("Cancelled")]
    public async Task UpdatePartialAsync_TerminalStatus_Throws(string status)
    {
        var sut = NewService(out var repo);
        var detail = DetailWithLines(status, lines: Array.Empty<(int, decimal, decimal)>());
        repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdatePartialAsync(TestTenantId, detail.Header.Id, EmptyPartialRequest(), null));
        Assert.Contains(status, ex.Message);
    }

    [Fact]
    public async Task UpdatePartialAsync_HappyPath_CallsAllOpsAndHeaderUpdate()
    {
        var sut = NewService(out var repo);
        var detail = DetailWithLines("Open",
            lines: new[] { (1, 10m, 0m), (2, 20m, 0m) });
        var poId = detail.Header.Id;
        var line1Id = detail.Lines[0].Id;

        repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        repo.Setup(r => r.UpdateHeaderAsync(It.IsAny<PurchaseOrder>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new PartialUpdatePurchaseOrderRequest(
            ExpectedDate: new DateOnly(2026, 7, 1),
            Notes: "updated",
            LineUpdates: new[] { new PartialUpdateLineEdit(line1Id, TestProductId, TestUomId, 50m) },
            LineInserts: new[] { new PartialUpdateLineInsert(3, TestProductId, TestUomId, 15m) },
            LineDeletes: new[] { detail.Lines[1].Id });

        await sut.UpdatePartialAsync(TestTenantId, poId, req, null);

        repo.Verify(r => r.UpdateHeaderAsync(
            It.IsAny<PurchaseOrder>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(r => r.UpdateLineAsync(
            line1Id, TestProductId, TestUomId, 50m, null, It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(r => r.InsertSingleLineAsync(
            poId, 3, TestProductId, TestUomId, 15m, null, It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(r => r.DeleteLineAsync(
            detail.Lines[1].Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdatePartialAsync_UpdateOnLockedLine_Throws_NoRepoCall()
    {
        var sut = NewService(out var repo);
        // Line 1 locked (ReceivedQty=5), Line 2 unlocked.
        var detail = DetailWithLines("Receiving",
            lines: new[] { (1, 10m, 5m), (2, 20m, 0m) });
        var poId = detail.Header.Id;
        var lockedLineId = detail.Lines[0].Id;

        repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var req = new PartialUpdatePurchaseOrderRequest(
            ExpectedDate: null, Notes: null,
            LineUpdates: new[] { new PartialUpdateLineEdit(lockedLineId, TestProductId, TestUomId, 99m) },
            LineInserts: Array.Empty<PartialUpdateLineInsert>(),
            LineDeletes: Array.Empty<Guid>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdatePartialAsync(TestTenantId, poId, req, null));
        Assert.Contains("receipts posted", ex.Message);
        repo.Verify(r => r.UpdateLineAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePartialAsync_DeleteOnLockedLine_Throws_NoRepoCall()
    {
        var sut = NewService(out var repo);
        var detail = DetailWithLines("Receiving",
            lines: new[] { (1, 10m, 5m) });
        var poId = detail.Header.Id;
        var lockedLineId = detail.Lines[0].Id;

        repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var req = new PartialUpdatePurchaseOrderRequest(
            ExpectedDate: null, Notes: null,
            LineUpdates: Array.Empty<PartialUpdateLineEdit>(),
            LineInserts: Array.Empty<PartialUpdateLineInsert>(),
            LineDeletes: new[] { lockedLineId });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdatePartialAsync(TestTenantId, poId, req, null));
        Assert.Contains("receipts posted", ex.Message);
        repo.Verify(r => r.DeleteLineAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePartialAsync_InsertCollidingLineNumber_Throws()
    {
        var sut = NewService(out var repo);
        var detail = DetailWithLines("Open",
            lines: new[] { (1, 10m, 0m), (2, 20m, 0m) });
        var poId = detail.Header.Id;

        repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var req = new PartialUpdatePurchaseOrderRequest(
            ExpectedDate: null, Notes: null,
            LineUpdates: Array.Empty<PartialUpdateLineEdit>(),
            // LineNumber 2 already exists → collision.
            LineInserts: new[] { new PartialUpdateLineInsert(2, TestProductId, TestUomId, 5m) },
            LineDeletes: Array.Empty<Guid>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdatePartialAsync(TestTenantId, poId, req, null));
        Assert.Contains("already exists", ex.Message);
        repo.Verify(r => r.InsertSingleLineAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<decimal>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdatePartialAsync_HeaderOnly_OnlyHeaderUpdated()
    {
        var sut = NewService(out var repo);
        var detail = DetailWithLines("Receiving",
            lines: new[] { (1, 10m, 5m) });  // single locked line
        var poId = detail.Header.Id;

        repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        repo.Setup(r => r.UpdateHeaderAsync(It.IsAny<PurchaseOrder>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.UpdatePartialAsync(TestTenantId, poId, EmptyPartialRequest(), null);

        repo.Verify(r => r.UpdateHeaderAsync(
            It.IsAny<PurchaseOrder>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(r => r.UpdateLineAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.InsertSingleLineAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<decimal>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repo.Verify(r => r.DeleteLineAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePartialAsync_InsertWithEmptyProductId_Throws()
    {
        var sut = NewService(out var repo);
        var detail = DetailWithLines("Open",
            lines: new[] { (1, 10m, 0m) });
        repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var req = new PartialUpdatePurchaseOrderRequest(
            ExpectedDate: null, Notes: null,
            LineUpdates: Array.Empty<PartialUpdateLineEdit>(),
            LineInserts: new[] {
                new PartialUpdateLineInsert(2, Guid.Empty, TestUomId, 5m)
            },
            LineDeletes: Array.Empty<Guid>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdatePartialAsync(TestTenantId, detail.Header.Id, req, null));
        Assert.Contains("ProductId is required", ex.Message);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static PartialUpdatePurchaseOrderRequest EmptyPartialRequest() =>
        new(
            ExpectedDate: null, Notes: null,
            LineUpdates: Array.Empty<PartialUpdateLineEdit>(),
            LineInserts: Array.Empty<PartialUpdateLineInsert>(),
            LineDeletes: Array.Empty<Guid>());

    // (LineNumber, ExpectedQuantity, ReceivedQuantity) per line. Status
    // derived from received vs expected — not authoritative for tests but
    // matches the shape SampleDetail would produce.
    private static PurchaseOrderDetail DetailWithLines(
        string headerStatus,
        IReadOnlyList<(int LineNumber, decimal Expected, decimal Received)> lines)
    {
        var poId = Guid.NewGuid();
        var lineList = lines.Select(t => new PurchaseOrderLine
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = poId,
            LineNumber = t.LineNumber,
            ProductId = Guid.NewGuid(),
            UomId = Guid.NewGuid(),
            ExpectedQuantity = t.Expected,
            ReceivedQuantity = t.Received,
            Status = t.Received == 0 ? "Open"
                    : t.Received >= t.Expected ? "Closed"
                    : "PartiallyReceived",
        }).ToList();

        var header = new PurchaseOrder
        {
            Id = poId,
            PoNumber = "PO-PART",
            OwnerId = TestOwnerId,
            WarehouseId = TestWarehouseId,
            Status = headerStatus,
        };
        return new PurchaseOrderDetail(header, lineList);
    }

    private static PurchaseOrderService NewService(out Mock<IPurchaseOrderRepository> repo)
    {
        repo = new Mock<IPurchaseOrderRepository>();
        var factory = new Mock<IPurchaseOrderRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);
        return new PurchaseOrderService(factory.Object,
            NullLogger<PurchaseOrderService>.Instance);
    }

    private static CreatePurchaseOrderRequest NewRequest() =>
        new(
            PoNumber: "PO-0001",
            OwnerId: TestOwnerId,
            WarehouseId: TestWarehouseId,
            ExpectedDate: new DateOnly(2026, 6, 1),
            Notes: null,
            Lines: new[]
            {
                new CreatePurchaseOrderLineRequest(1, TestProductId, TestUomId, 100),
            });

    private static PurchaseOrderDetail NewDetailStub()
    {
        var header = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            PoNumber = "PO-0001",
            OwnerId = TestOwnerId,
            WarehouseId = TestWarehouseId,
            Status = "Open",
        };
        return new PurchaseOrderDetail(header, Array.Empty<PurchaseOrderLine>());
    }
}
