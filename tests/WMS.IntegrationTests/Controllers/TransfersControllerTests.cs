using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Inventory;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Inventory;
using WMS.Web.Controllers;
using WMS.Web.Models.Inventory;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

// Phase 13 (ADR-012) — TransfersController tests. Same BuildController
// pattern as Adjustments / CycleCounts.
public class TransfersControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        TransfersController Controller,
        Mock<ITransferOrderRepository> Repo,
        Mock<ITransferOrderService> Service,
        Mock<IValidator<TransferOrderCreateViewModel>> CreateValidator,
        Mock<IValidator<CancelTransferViewModel>> CancelValidator,
        Mock<IValidator<MarkLostTransferViewModel>> LostValidator,
        Guid CurrentUserId);

    private static Build BuildController()
    {
        var repo = new Mock<ITransferOrderRepository>();
        var factory = new Mock<ITransferOrderRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<TransferOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderStatusCounts(0, 0, 0, 0, 0, 0, 0, 0));
        repo.Setup(r => r.GetLineRowsByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransferOrderLineRow>());

        var service = new Mock<ITransferOrderService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);
        currentUser.SetupGet(u => u.WarehouseId).Returns((Guid?)null);

        var warehouseRepo = new Mock<IWarehouseRepository>();
        warehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo>
            {
                new(Guid.NewGuid(), "WH-MAIN", "Main"),
                new(Guid.NewGuid(), "WH-DM01", "DM01"),
            });
        var warehouseFactory = new Mock<IWarehouseRepositoryFactory>();
        warehouseFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(warehouseRepo.Object);

        var productRepo = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem> { new(Guid.NewGuid(), "P-1", "Product 1") });
        var productFactory = new Mock<IProductRepositoryFactory>();
        productFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(productRepo.Object);

        var ownerRepo = new Mock<IOwnerRepository>();
        ownerRepo.Setup(r => r.GetActiveSuppliersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem> { new(Guid.NewGuid(), "OWN-1", "Owner 1") });
        var ownerFactory = new Mock<IOwnerRepositoryFactory>();
        ownerFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(ownerRepo.Object);

        var uomRepo = new Mock<IUomRepository>();
        uomRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem> { new(Guid.NewGuid(), "EA", "Each") });
        var uomFactory = new Mock<IUomRepositoryFactory>();
        uomFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(uomRepo.Object);

        var locationRepo = new Mock<ILocationRepository>();
        locationRepo.Setup(r => r.GetActiveByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(Guid.NewGuid(), "BIN-A1", "Bin A1"),
            });
        var locationFactory = new Mock<ILocationRepositoryFactory>();
        locationFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(locationRepo.Object);

        var createValidator = new Mock<IValidator<TransferOrderCreateViewModel>>();
        createValidator.Setup(v => v.ValidateAsync(
                It.IsAny<TransferOrderCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var cancelValidator = new Mock<IValidator<CancelTransferViewModel>>();
        cancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelTransferViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var lostValidator = new Mock<IValidator<MarkLostTransferViewModel>>();
        lostValidator.Setup(v => v.ValidateAsync(
                It.IsAny<MarkLostTransferViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var ctrl = new TransfersController(
            factory.Object, service.Object,
            warehouseFactory.Object, productFactory.Object,
            ownerFactory.Object, uomFactory.Object, locationFactory.Object,
            tenant.Object, currentUser.Object,
            createValidator.Object, cancelValidator.Object, lostValidator.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, repo, service, createValidator,
            cancelValidator, lostValidator, currentUserId);
    }

    private static TransferOrder SampleHeader(
        string status = "Draft", Guid? requestedBy = null) => new()
    {
        Id = Guid.NewGuid(),
        TransferNumber = "TRN-X",
        FromWarehouseId = Guid.NewGuid(),
        ToWarehouseId = Guid.NewGuid(),
        Status = status,
        Reason = "Rebalance",
        RequestedBy = requestedBy ?? Guid.NewGuid(),
        RequestedAt = DateTime.UtcNow,
        // Stamp the matching audit fields so a CK_TransferOrders_AuditMatchesStatus
        // round-trip read wouldn't fail; tests don't hit DB but it keeps the
        // sample legible.
        SubmittedAt = status is "Submitted" or "Approved" or "InTransit" or "Received"
            ? DateTime.UtcNow : null,
        ApprovedAt  = status is "Approved" or "InTransit" or "Received" ? DateTime.UtcNow : null,
        DispatchedAt = status is "InTransit" or "Received" ? DateTime.UtcNow : null,
        ReceivedAt   = status == "Received" ? DateTime.UtcNow : null,
        CancelledAt  = status == "Cancelled" ? DateTime.UtcNow : null,
        LostAt       = status == "Lost"       ? DateTime.UtcNow : null,
    };

    private static TransferOrderDetail SampleDetail(
        string status = "Draft", Guid? requestedBy = null) =>
        new(SampleHeader(status, requestedBy), new List<TransferOrderLine>());

    [Fact]
    public void Index_ReturnsView()
    {
        var b = BuildController();
        Assert.IsType<ViewResult>(b.Controller.Index());
    }

    [Fact]
    public async Task GetData_ReturnsJsonWithRowsAndCounts()
    {
        var b = BuildController();
        b.Repo.Setup(r => r.GetPagedAsync(
                It.IsAny<TransferOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<TransferOrderListRow>
            {
                Items = new(), Total = 0, Page = 1, PageSize = 20, TotalPages = 0,
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<TransferOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferOrderStatusCounts(
                All: 12, Draft: 1, Submitted: 2, Approved: 3, InTransit: 2,
                Received: 3, Cancelled: 1, Lost: 0));

        var json = Assert.IsType<JsonResult>(await b.Controller.GetData());
        var envelope = json.Value!;
        var counts = envelope.GetType().GetProperty("counts")!.GetValue(envelope)!;
        Assert.Equal(12, counts.GetType().GetProperty("all")!.GetValue(counts));
        Assert.Equal(2, counts.GetType().GetProperty("intransit")!.GetValue(counts));
        Assert.Equal(0, counts.GetType().GetProperty("lost")!.GetValue(counts));
    }

    [Fact]
    public async Task Create_Get_PopulatesLookups()
    {
        var b = BuildController();
        var view = Assert.IsType<ViewResult>(await b.Controller.Create(default));
        var vm = Assert.IsType<TransferOrderCreateViewModel>(view.Model);
        Assert.Equal(2, vm.Warehouses.Count);
        Assert.Single(vm.Products);
        Assert.Single(vm.Owners);
        Assert.Single(vm.Uoms);
        Assert.Single(vm.Lines); // starter line
    }

    [Fact]
    public async Task Create_Post_ValidationFails_ReturnsViewWithErrors()
    {
        var b = BuildController();
        b.CreateValidator.Setup(v => v.ValidateAsync(
                It.IsAny<TransferOrderCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Reason is required."),
            }));

        var result = await b.Controller.Create(new TransferOrderCreateViewModel(), default);
        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
        b.Service.Verify(s => s.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<CreateTransferOrderRequest>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_HappyPath_RedirectsToDetail()
    {
        var b = BuildController();
        var saved = SampleDetail();
        b.Service.Setup(s => s.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<CreateTransferOrderRequest>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var vm = new TransferOrderCreateViewModel
        {
            FromWarehouseId = Guid.NewGuid(),
            ToWarehouseId = Guid.NewGuid(),
            Reason = "Rebalance",
            Lines = new()
            {
                new() { LineNumber = 1, ProductId = Guid.NewGuid(),
                        OwnerId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                        FromLocationId = Guid.NewGuid(), ToLocationId = Guid.NewGuid(),
                        QtyRequested = 5m },
            },
        };

        var result = await b.Controller.Create(vm, default);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(b.Controller.Detail), redirect.ActionName);
        Assert.Equal(saved.Header.Id, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransferOrderDetail?)null);

        Assert.IsType<NotFoundResult>(
            await b.Controller.Detail(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Detail_Draft_OnlySubmitAndCancelEnabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Draft");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Submit").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Approve").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Dispatch").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Receive").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Cancel").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Mark as lost").Enabled);
    }

    [Fact]
    public async Task Detail_SubmittedByOther_ApproveEnabled_NoSelfApprovalBanner()
    {
        var b = BuildController();
        var detail = SampleDetail("Submitted", requestedBy: Guid.NewGuid());
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Approve").Enabled);
        Assert.True((bool)view.ViewData["CanApprove"]!);
        Assert.False((bool)view.ViewData["SelfApproval"]!);
    }

    [Fact]
    public async Task Detail_SubmittedBySelf_ApproveDisabled_SelfApprovalBanner()
    {
        var b = BuildController();
        var detail = SampleDetail("Submitted", requestedBy: b.CurrentUserId);
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.False(vm.QuickActions.First(a => a.Label == "Approve").Enabled);
        Assert.True((bool)view.ViewData["SelfApproval"]!);
    }

    [Fact]
    public async Task Detail_Approved_DispatchEnabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Approved");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Dispatch").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Receive").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Mark as lost").Enabled);
    }

    [Fact]
    public async Task Detail_InTransit_ReceiveAndMarkLostEnabled_CancelDisabled()
    {
        var b = BuildController();
        var detail = SampleDetail("InTransit");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Receive").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Mark as lost").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Cancel").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Dispatch").Enabled);
    }

    [Fact]
    public async Task Detail_Received_AllActionsDisabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Received");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.All(vm.QuickActions, qa => Assert.False(qa.Enabled));
    }

    [Fact]
    public async Task Submit_HappyPath_RedirectsWithMessage()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Submit(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("submitted", (string)b.Controller.TempData["TransferMessage"]!);
    }

    [Fact]
    public async Task Approve_ServiceThrows_SurfacesError()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.ApproveAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Self-approval blocked."));

        var result = await b.Controller.Approve(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Self-approval blocked.", b.Controller.TempData["TransferError"]);
    }

    [Fact]
    public async Task Dispatch_ZeroAcrossAllLines_SurfacesError_NoServiceCall()
    {
        var b = BuildController();
        var id = Guid.NewGuid();

        var vm = new DispatchTransferViewModel
        {
            Lines = new()
            {
                new() { LineId = Guid.NewGuid(), QtyDispatched = 0m },
                new() { LineId = Guid.NewGuid(), QtyDispatched = 0m },
            },
        };

        var result = await b.Controller.Dispatch(id, vm, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("at least one", (string)b.Controller.TempData["TransferError"]!);
        b.Service.Verify(s => s.DispatchAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<DispatchLineRequest>>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_HappyPath_FiltersZeroLines_CallsServiceWithPositiveOnly()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        var keepLineId = Guid.NewGuid();

        IReadOnlyList<DispatchLineRequest>? captured = null;
        b.Service.Setup(s => s.DispatchAsync(
                It.IsAny<Guid>(), id,
                It.IsAny<IReadOnlyList<DispatchLineRequest>>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, IReadOnlyList<DispatchLineRequest>, Guid, CancellationToken>(
                (_, _, l, _, _) => captured = l)
            .ReturnsAsync(true);

        var vm = new DispatchTransferViewModel
        {
            Lines = new()
            {
                new() { LineId = Guid.NewGuid(), QtyDispatched = 0m }, // skipped
                new() { LineId = keepLineId,    QtyDispatched = 4m }, // kept
            },
        };

        var result = await b.Controller.Dispatch(id, vm, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(captured);
        Assert.Single(captured!);
        Assert.Equal(keepLineId, captured![0].LineId);
        Assert.Equal(4m, captured[0].QtyDispatched);
    }

    [Fact]
    public async Task Receive_HappyPath_PostsAllLinesToService()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        var l1 = Guid.NewGuid();
        var l2 = Guid.NewGuid();

        IReadOnlyList<ReceiveLineRequest>? captured = null;
        b.Service.Setup(s => s.ReceiveAsync(
                It.IsAny<Guid>(), id,
                It.IsAny<IReadOnlyList<ReceiveLineRequest>>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, IReadOnlyList<ReceiveLineRequest>, Guid, CancellationToken>(
                (_, _, l, _, _) => captured = l)
            .ReturnsAsync(true);

        var vm = new ReceiveTransferViewModel
        {
            Lines = new()
            {
                new() { LineId = l1, QtyReceived = 3m },
                new() { LineId = l2, QtyReceived = 0m }, // zero allowed for Receive
            },
        };

        var result = await b.Controller.Receive(id, vm, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);
    }

    [Fact]
    public async Task Cancel_ValidationFails_NoServiceCall()
    {
        var b = BuildController();
        b.CancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelTransferViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Cancellation reason is required."),
            }));

        var result = await b.Controller.Cancel(Guid.NewGuid(), new CancelTransferViewModel(), default);
        Assert.IsType<RedirectToActionResult>(result);
        b.Service.Verify(s => s.CancelAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Cancellation reason is required.",
            b.Controller.TempData["TransferError"]);
    }

    [Fact]
    public async Task MarkLost_HappyPath_CallsServiceWithTrimmedReason()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.MarkLostAsync(
                It.IsAny<Guid>(), id, "stolen", b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new MarkLostTransferViewModel { Reason = "  stolen  " };
        var result = await b.Controller.MarkLost(id, vm, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("lost", (string)b.Controller.TempData["TransferMessage"]!);
    }

    [Theory]
    [InlineData("Draft",     "draft",     "neutral")]
    [InlineData("Submitted", "submitted", "info")]
    [InlineData("Approved",  "approved",  "info")]
    [InlineData("InTransit", "intransit", "warning")]
    [InlineData("Received",  "received",  "success")]
    [InlineData("Cancelled", "cancelled", "neutral")]
    [InlineData("Lost",      "lost",      "danger")]
    public void StatusMapper_RoundTrips(string db, string wire, string variant)
    {
        Assert.Equal(wire, WMS.Web.Services.Mappers.TransferOrderStatusMapper.ToWire(db));
        Assert.Equal(db, WMS.Web.Services.Mappers.TransferOrderStatusMapper.FromWire(wire));
        Assert.Equal(variant, WMS.Web.Services.Mappers.TransferOrderStatusMapper.ToBadgeVariant(db));
    }
}
