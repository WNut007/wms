using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Primitives;
using Moq;
using WMS.BLL.Services.Inbound;
using WMS.Common.Auth;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Inbound;
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
        Mock<IStockMovementRepository> Movements,
        Mock<IReceivingHeaderService> Service,
        Mock<IValidator<CancelReceivingViewModel>> CancelValidator);

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

        // TD-028 — chip-count aggregates. Default zero-counts so GetData
        // tests that don't care about counts still get a populated
        // ReceivingStatusCounts (controller dereferences it).
        repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<ReceivingFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceivingStatusCounts(0, 0, 0, 0));

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var docs = Mock.Of<IDocumentStorageService>(d =>
            d.ListByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<DocumentMetadata>()));

        // Phase 10B (TD-023) — IReceivingHeaderService for Cancel.
        var service = new Mock<IReceivingHeaderService>();
        var cancelValidator = new Mock<IValidator<CancelReceivingViewModel>>();
        cancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelReceivingViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(Guid.NewGuid());

        var ctrl = new ReceivingController(
            factory.Object, movementFactory.Object, tenant.Object, docs,
            service.Object, cancelValidator.Object, currentUser.Object);

        // TempData is required by Cancel POST → redirect.
        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, repo, movements, service, cancelValidator);
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

    // TD-028 — chip counts surface on the JSON envelope.
    [Fact]
    public async Task GetData_ReturnsCountsAlongsideRows()
    {
        var b = BuildController();
        b.Repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<ReceivingFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceivingStatusCounts(
                All: 12, Draft: 3, Posted: 7, Cancelled: 2));
        b.Repo.Setup(r => r.GetPagedAsync(
                It.IsAny<ReceivingFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ReceivingListRow>
            {
                Items = new(), Total = 12, Page = 1, PageSize = 20, TotalPages = 1,
            });

        var json = Assert.IsType<JsonResult>(await b.Controller.GetData());
        var envelope = json.Value!;
        var counts = envelope.GetType().GetProperty("counts")!.GetValue(envelope)!;
        Assert.Equal(12, counts.GetType().GetProperty("all")!.GetValue(counts));
        Assert.Equal(3,  counts.GetType().GetProperty("draft")!.GetValue(counts));
        Assert.Equal(7,  counts.GetType().GetProperty("posted")!.GetValue(counts));
        Assert.Equal(2,  counts.GetType().GetProperty("cancelled")!.GetValue(counts));
    }

    // TD-028 — counts request shares the same filter as the rows query
    // (so search is honoured); but per repo contract the counts query
    // ignores Status. Confirms the controller passes the filter through.
    [Fact]
    public async Task GetData_PassesSameFilterToCountsAsToRows()
    {
        var b = BuildController();
        ReceivingFilter? capturedRows = null;
        ReceivingFilter? capturedCounts = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<ReceivingFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingFilter, CancellationToken>((f, _) => capturedRows = f)
            .ReturnsAsync(new PagedResult<ReceivingListRow>());
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<ReceivingFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingFilter, CancellationToken>((f, _) => capturedCounts = f)
            .ReturnsAsync(new ReceivingStatusCounts(0, 0, 0, 0));

        await b.Controller.GetData(search: "GR-7", status: "posted");

        Assert.NotNull(capturedRows);
        Assert.NotNull(capturedCounts);
        Assert.Equal(capturedRows!.Search, capturedCounts!.Search);
        Assert.Equal(capturedRows.Status,  capturedCounts.Status);
    }

    // ================================================================
    // Phase 10B (TD-023) — Cancel-receipt endpoint
    // ================================================================

    [Fact]
    public async Task Cancel_HappyPath_CallsService_AndRedirectsToDetail()
    {
        var b = BuildController();
        var detail = SampleDetail("GR-CXL-1", "Posted");
        b.Repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.Service.Setup(s => s.CancelReceivingAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new CancelReceivingViewModel { Reason = "stock damaged" };
        var result = await b.Controller.Cancel(detail.Header.Id, vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(b.Controller.Detail), redirect.ActionName);
        Assert.Equal("GR-CXL-1", redirect.RouteValues!["number"]);
        Assert.Contains("cancelled", (string)b.Controller.TempData["CancelMessage"]!);
    }

    [Fact]
    public async Task Cancel_ValidationFails_DoesNotCallService_AndSetsError()
    {
        var b = BuildController();
        var headerId = Guid.NewGuid();
        b.Repo.Setup(r => r.GetByIdAsync(headerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleDetail("GR-X", "Posted") with { Header = new ReceivingHeader
                { Id = headerId, ReceivingNumber = "GR-X", Status = "Posted",
                  WarehouseId = Guid.NewGuid(), ReceivedAt = DateTime.UtcNow } });

        b.CancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelReceivingViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Reason is required."),
            }));

        var result = await b.Controller.Cancel(headerId, new CancelReceivingViewModel(), default);

        Assert.IsType<RedirectToActionResult>(result);
        b.Service.Verify(s => s.CancelReceivingAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Reason is required.", b.Controller.TempData["CancelError"]);
    }

    [Fact]
    public async Task Cancel_ServiceReturnsFalse_SurfacesAlreadyCancelledNotice()
    {
        var b = BuildController();
        var detail = SampleDetail("GR-ALREADY", "Cancelled");
        b.Repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.Service.Setup(s => s.CancelReceivingAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);  // idempotent — already cancelled

        var vm = new CancelReceivingViewModel { Reason = "duplicate cancel" };
        var result = await b.Controller.Cancel(detail.Header.Id, vm, default);

        Assert.IsType<RedirectToActionResult>(result);
        var msg = (string)b.Controller.TempData["CancelMessage"]!;
        Assert.Contains("already cancelled", msg);
    }

    [Fact]
    public async Task Cancel_ServiceThrowsInvalidOperation_SurfacesErrorBanner()
    {
        var b = BuildController();
        var detail = SampleDetail("GR-UNDERFLOW", "Posted");
        b.Repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.Service.Setup(s => s.CancelReceivingAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Cannot cancel — stock has been consumed."));

        var vm = new CancelReceivingViewModel { Reason = "supplier error" };
        var result = await b.Controller.Cancel(detail.Header.Id, vm, default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Cannot cancel — stock has been consumed.",
            b.Controller.TempData["CancelError"]);
    }

    // Detail surfaces cancellation audit fields when the header is
    // Cancelled (Phase 10B UI wiring).
    [Fact]
    public async Task Detail_CancelledReceipt_SurfacesAuditFieldsOnVm()
    {
        var b = BuildController();
        var headerId = Guid.NewGuid();
        var cancelledAt = DateTime.UtcNow.AddMinutes(-15);
        var header = new ReceivingHeader
        {
            Id = headerId,
            ReceivingNumber = "GR-DONE",
            PurchaseOrderId = null,
            WarehouseId = Guid.NewGuid(),
            ReceivedAt = DateTime.UtcNow.AddHours(-2),
            Status = "Cancelled",
            CancelledBy = Guid.NewGuid(),
            CancelledAt = cancelledAt,
            CancelReason = "supplier sent wrong SKU",
        };
        var detail = new ReceivingDetail(header, Array.Empty<ReceivingLine>());

        b.Repo.Setup(r => r.GetByNumberAsync("GR-DONE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await b.Controller.Detail("GR-DONE", default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<WMS.Web.ViewModels.Detail.DetailPageViewModel>(view.Model);

        // Cancel reason appears in OverviewFields.
        Assert.Contains(vm.OverviewFields,
            kv => kv.Key == "Cancel reason" && kv.Value.Contains("wrong SKU"));
        // Cancelled-at relative appears in Properties.
        Assert.Contains(vm.Properties, kv => kv.Key == "Cancelled");

        // Cancel QuickAction is disabled (already cancelled).
        var cancelAction = vm.QuickActions.First(a => a.Label == "Cancel receipt");
        Assert.False(cancelAction.Enabled);
    }

    [Fact]
    public async Task Detail_PostedReceipt_CancelQuickActionIsEnabled()
    {
        var b = BuildController();
        var detail = SampleDetail("GR-POSTED", "Posted");
        b.Repo.Setup(r => r.GetByNumberAsync("GR-POSTED", It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail("GR-POSTED", default);
        var vm = Assert.IsType<WMS.Web.ViewModels.Detail.DetailPageViewModel>(view.Model);

        var cancelAction = vm.QuickActions.First(a => a.Label == "Cancel receipt");
        Assert.True(cancelAction.Enabled);
        Assert.Equal("#cancel-modal", cancelAction.Url);
        Assert.True((bool)view.ViewData["IsPosted"]!);
    }
}
