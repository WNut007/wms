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
