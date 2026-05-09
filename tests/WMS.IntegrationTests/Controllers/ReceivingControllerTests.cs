using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inbound;
using WMS.Web.Controllers;
using WMS.Web.Services.Storage;

namespace WMS.IntegrationTests.Controllers;

// Phase 9C — receiving list/detail/print controller tests.
// Read-only surface; no admin write paths to test (Cancel deferred
// to TD-023, Edit-Draft-Promote to TD-027).
public class ReceivingControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record Build(
        ReceivingController Controller,
        Mock<IReceivingHeaderRepository> Repo,
        Mock<IStockMovementRepository> Movements);

    private static Build BuildController()
    {
        var repo            = new Mock<IReceivingHeaderRepository>();
        var factory         = new Mock<IReceivingHeaderRepositoryFactory>();
        var movements       = new Mock<IStockMovementRepository>();
        var movementFactory = new Mock<IStockMovementRepositoryFactory>();

        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);
        movementFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(movements.Object);

        // Default empty movements list — Detail tests that don't care
        // about Activity see the empty-state path.
        movements.Setup(m => m.GetByReceivingHeaderAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockMovementListRow>());

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var docs = Mock.Of<IDocumentStorageService>(d =>
            d.ListByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<DocumentMetadata>()));

        var ctrl = new ReceivingController(factory.Object, movementFactory.Object, tenant.Object, docs);
        return new Build(ctrl, repo, movements);
    }

    private static ReceivingDetail SampleDetail(string number = "GR-TEST-001", string status = "Posted", bool blind = false)
    {
        var headerId = Guid.NewGuid();
        var header = new ReceivingHeader
        {
            Id = headerId,
            ReceivingNumber = number,
            PurchaseOrderId = blind ? (Guid?)null : Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            ReceivedAt = DateTime.UtcNow.AddHours(-1),
            Status = status,
            Notes = "Test",
        };
        var lines = new List<ReceivingLine>
        {
            new()
            {
                Id = Guid.NewGuid(), ReceivingHeaderId = headerId, LineNumber = 1,
                ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(), LocationId = Guid.NewGuid(),
                ReceivedQuantity = 7m,
            },
        };
        return new ReceivingDetail(header, lines);
    }

    [Fact]
    public void Index_ReturnsView()
    {
        var b = BuildController();
        var result = b.Controller.Index();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task GetData_PassesFilterToRepo_AndReturnsJson()
    {
        var b = BuildController();
        ReceivingFilter? captured = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<ReceivingFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ReceivingListRow>
            {
                Items = new(), Total = 0, Page = 1, PageSize = 20, TotalPages = 0,
            });

        var result = await b.Controller.GetData(
            page: 2, pageSize: 50, search: "GR-A", status: "draft",
            warehouse: "WH-MAIN", sortBy: "receivedAt", sortDesc: false);

        Assert.IsType<JsonResult>(result);
        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Page);
        Assert.Equal(50, captured.PageSize);
        Assert.Equal("GR-A", captured.Search);
        Assert.Equal("Draft", captured.Status);  // wire→DB by mapper
        Assert.Equal("WH-MAIN", captured.WarehouseCode);
        Assert.False(captured.SortDesc);
    }

    [Fact]
    public async Task GetData_StatusAll_LeavesFilterNull()
    {
        var b = BuildController();
        ReceivingFilter? captured = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<ReceivingFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ReceivingListRow>());

        await b.Controller.GetData(status: "all");
        Assert.Null(captured!.Status);
    }

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.Repo.Setup(r => r.GetByNumberAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReceivingDetail?)null);

        var result = await b.Controller.Detail("MISSING", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_PostedReceipt_ReturnsViewWithDetailLayoutVm()
    {
        var b = BuildController();
        var detail = SampleDetail("GR-001", "Posted");
        b.Repo.Setup(r => r.GetByNumberAsync("GR-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await b.Controller.Detail("GR-001", default);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Shared/_DetailLayout.cshtml", view.ViewName);
    }

    [Fact]
    public async Task Detail_BlindReceipt_PoQuickActionDisabled()
    {
        var b = BuildController();
        var detail = SampleDetail("GR-BLIND", "Posted", blind: true);
        b.Repo.Setup(r => r.GetByNumberAsync("GR-BLIND", It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await b.Controller.Detail("GR-BLIND", default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<WMS.Web.ViewModels.Detail.DetailPageViewModel>(view.Model);
        var viewPo = vm.QuickActions.FirstOrDefault(a => a.Label == "View PO");
        Assert.NotNull(viewPo);
        Assert.False(viewPo!.Enabled);  // disabled when no PO
    }

    [Fact]
    public async Task Print_NotFound_Returns404()
    {
        var b = BuildController();
        b.Repo.Setup(r => r.GetByNumberAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReceivingDetail?)null);

        var result = await b.Controller.Print("MISSING", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Print_Found_ReturnsViewWithReceivingDetailModel()
    {
        var b = BuildController();
        var detail = SampleDetail();
        b.Repo.Setup(r => r.GetByNumberAsync(detail.Header.ReceivingNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await b.Controller.Print(detail.Header.ReceivingNumber, default);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(detail, view.Model);
    }

    [Theory]
    [InlineData("Draft",     "draft")]
    [InlineData("Posted",    "posted")]
    [InlineData("Cancelled", "cancelled")]
    public void StatusMapper_RoundTrips(string db, string wire)
    {
        Assert.Equal(wire, WMS.Web.Services.Mappers.ReceivingStatusMapper.ToWire(db));
        Assert.Equal(db, WMS.Web.Services.Mappers.ReceivingStatusMapper.FromWire(wire));
    }
}
