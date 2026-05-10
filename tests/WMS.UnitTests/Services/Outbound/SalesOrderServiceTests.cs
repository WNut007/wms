using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.UnitTests.Services.Outbound;

// Phase 14A — SalesOrderService unit tests. MVP foundation has no
// Stock writes / TransactionScope, so coverage focuses on validation,
// state-transition gating, and the ReplaceLines-on-non-Draft guard.
public class SalesOrderServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid UomId = Guid.NewGuid();

    private static SalesOrderService NewService(out Mock<ISalesOrderRepository> repo)
    {
        repo = new Mock<ISalesOrderRepository>();
        var factory = new Mock<ISalesOrderRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        // Phase 14B added IAllocationService constructor dep — default-mock
        // internally per feedback_test_build_helper_pattern.md so the
        // public NewService signature stays stable for existing tests.
        var allocationService = new Mock<IAllocationService>();
        allocationService.Setup(s => s.ReleaseAllForSalesOrderAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        return new SalesOrderService(
            factory.Object,
            allocationService.Object,
            NullLogger<SalesOrderService>.Instance);
    }

    private static CreateSalesOrderRequest NewRequest(
        IReadOnlyList<CreateSalesOrderLineRequest>? lines = null,
        Guid? customer = null,
        Guid? warehouse = null) =>
        new(
            CustomerId: customer ?? CustomerId,
            WarehouseId: warehouse ?? WarehouseId,
            OrderDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
            RequestedShipDate: null,
            Notes: null,
            Lines: lines ?? new List<CreateSalesOrderLineRequest> { NewLine(1) });

    private static CreateSalesOrderLineRequest NewLine(
        int n, decimal qty = 5m, decimal? unitPrice = null) =>
        new(LineNumber: n, ProductId: ProductId, OwnerId: OwnerId, UomId: UomId,
            OrderedQuantity: qty, UnitPrice: unitPrice, Notes: null);

    private static SalesOrder NewHeader(
        Guid id, string status = "Draft", string number = "SO-X") => new()
    {
        Id = id,
        SoNumber = number,
        CustomerId = CustomerId,
        WarehouseId = WarehouseId,
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Status = status,
    };

    private static SalesOrderLine NewDomainLine(Guid lineId, Guid soId) => new()
    {
        Id = lineId, SalesOrderId = soId, LineNumber = 1,
        ProductId = ProductId, OwnerId = OwnerId, UomId = UomId,
        OrderedQuantity = 5m,
    };

    // ================================================================
    // CreateAsync — validation
    // ================================================================

    [Fact]
    public async Task Create_EmptyCustomerId_Throws()
    {
        var sut = NewService(out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(customer: Guid.Empty), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_EmptyWarehouseId_Throws()
    {
        var sut = NewService(out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(warehouse: Guid.Empty), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_NoLines_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest(lines: new List<CreateSalesOrderLineRequest>());
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_DuplicateLineNumbers_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest(lines: new List<CreateSalesOrderLineRequest>
        {
            NewLine(1), NewLine(1), // duplicate
        });
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_NonPositiveQuantity_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest(lines: new List<CreateSalesOrderLineRequest>
        {
            NewLine(1, qty: 0m),
        });
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_NegativeUnitPrice_Throws()
    {
        var sut = NewService(out _);
        var req = NewRequest(lines: new List<CreateSalesOrderLineRequest>
        {
            NewLine(1, unitPrice: -1m),
        });
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_HappyPath_AssignsNumberAndPersists()
    {
        var sut = NewService(out var repo);
        var requesterId = Guid.NewGuid();

        repo.Setup(r => r.CountForDatePrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        SalesOrder? capturedHeader = null;
        IReadOnlyList<SalesOrderLine>? capturedLines = null;
        repo.Setup(r => r.CreateAsync(
                It.IsAny<SalesOrder>(),
                It.IsAny<IReadOnlyList<SalesOrderLine>>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<SalesOrder, IReadOnlyList<SalesOrderLine>, Guid?, CancellationToken>(
                (h, l, _, _) => { capturedHeader = h; capturedLines = l; })
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new SalesOrderDetail(
                    capturedHeader ?? new SalesOrder { Id = id },
                    capturedLines ?? new List<SalesOrderLine>()));

        var saved = await sut.CreateAsync(TenantId, NewRequest(), requesterId);

        Assert.NotNull(capturedHeader);
        Assert.Equal("Draft", capturedHeader!.Status);
        Assert.Equal(CustomerId, capturedHeader.CustomerId);
        var prefix = $"SO-{DateTime.UtcNow:yyyyMMdd}-";
        Assert.StartsWith(prefix, capturedHeader.SoNumber);
        Assert.EndsWith("0005", capturedHeader.SoNumber);  // 4 existing + 1
        Assert.Single(capturedLines!);
    }

    // ================================================================
    // SubmitAsync
    // ================================================================

    [Fact]
    public async Task Submit_AlreadyOpen_ReturnsFalse()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Open"),
                new List<SalesOrderLine> { NewDomainLine(Guid.NewGuid(), id) }));

        var changed = await sut.SubmitAsync(TenantId, id, Guid.NewGuid());
        Assert.False(changed);
    }

    [Fact]
    public async Task Submit_FromCancelled_Throws()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Cancelled"),
                new List<SalesOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SubmitAsync(TenantId, id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_ZeroLines_Throws()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Draft"),
                new List<SalesOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SubmitAsync(TenantId, id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_HappyPath_FlipsStatus()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Draft"),
                new List<SalesOrderLine> { NewDomainLine(Guid.NewGuid(), id) }));
        repo.Setup(r => r.SetStatusAsync(id, "Draft", "Open", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.SubmitAsync(TenantId, id, userId);
        Assert.True(ok);
        repo.Verify(r => r.SetStatusAsync(
            id, "Draft", "Open", userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ================================================================
    // CancelAsync
    // ================================================================

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsFalse()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Cancelled"),
                new List<SalesOrderLine>()));

        var changed = await sut.CancelAsync(TenantId, id, Guid.NewGuid());
        Assert.False(changed);
    }

    [Fact]
    public async Task Cancel_FromOpen_TriesEachSourceState()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Open"),
                new List<SalesOrderLine>()));

        // Draft no-ops, Open hits.
        repo.Setup(r => r.SetStatusAsync(id, "Draft", "Cancelled", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.SetStatusAsync(id, "Open", "Cancelled", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.CancelAsync(TenantId, id, userId);
        Assert.True(ok);
        repo.Verify(r => r.SetStatusAsync(
            id, "Open", "Cancelled", userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ================================================================
    // UpdateAsync — ReplaceLines gating
    // ================================================================

    [Fact]
    public async Task Update_NonExistent_Throws()
    {
        var sut = NewService(out var repo);
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SalesOrderDetail?)null);

        var req = new UpdateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, null, false,
            Array.Empty<UpdateSalesOrderLineRequest>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.UpdateAsync(TenantId, Guid.NewGuid(), req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Update_OnCancelled_Throws()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Cancelled"), new List<SalesOrderLine>()));

        var req = new UpdateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, null, false,
            Array.Empty<UpdateSalesOrderLineRequest>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.UpdateAsync(TenantId, id, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Update_ReplaceLinesOnNonDraft_Throws()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Open"), new List<SalesOrderLine>()));

        var req = new UpdateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, null, ReplaceLines: true,
            Lines: new List<UpdateSalesOrderLineRequest>
            {
                new(1, ProductId, OwnerId, UomId, 5m, null, null),
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.UpdateAsync(TenantId, id, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Update_HeaderOnlyEdit_OnOpen_Succeeds()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var existing = new SalesOrderDetail(
            NewHeader(id, "Open"), new List<SalesOrderLine>());
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        repo.Setup(r => r.UpdateHeaderAsync(
                It.IsAny<SalesOrder>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new UpdateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, "updated", ReplaceLines: false,
            Lines: Array.Empty<UpdateSalesOrderLineRequest>());

        var detail = await sut.UpdateAsync(TenantId, id, req, userId);

        Assert.NotNull(detail);
        repo.Verify(r => r.UpdateHeaderAsync(
            It.IsAny<SalesOrder>(), userId, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.ReplaceLinesAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<SalesOrderLine>>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_ReplaceLinesOnDraft_Succeeds()
    {
        var sut = NewService(out var repo);
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewHeader(id, "Draft"), new List<SalesOrderLine>()));
        repo.Setup(r => r.UpdateHeaderAsync(
                It.IsAny<SalesOrder>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new UpdateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, null, ReplaceLines: true,
            Lines: new List<UpdateSalesOrderLineRequest>
            {
                new(1, ProductId, OwnerId, UomId, 7m, null, null),
            });

        await sut.UpdateAsync(TenantId, id, req, userId);

        repo.Verify(r => r.ReplaceLinesAsync(
            id, It.IsAny<IReadOnlyList<SalesOrderLine>>(),
            userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
