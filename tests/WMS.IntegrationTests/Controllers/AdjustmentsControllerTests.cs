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

// Phase 11A — AdjustmentsController tests. Mirrors the
// BuildController pattern from ReceivingControllerTests.
public class AdjustmentsControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        AdjustmentsController Controller,
        Mock<IAdjustmentRepository> Repo,
        Mock<IAdjustmentService> Service,
        Mock<IValidator<AdjustmentCreateViewModel>> CreateValidator,
        Mock<IValidator<RejectAdjustmentViewModel>> RejectValidator,
        Guid CurrentUserId);

    private static Build BuildController()
    {
        var repo = new Mock<IAdjustmentRepository>();
        var factory = new Mock<IAdjustmentRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        // Default zero-counts so GetData tests can dereference safely.
        repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<AdjustmentFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdjustmentStatusCounts(0, 0, 0, 0));

        var service = new Mock<IAdjustmentService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);
        currentUser.SetupGet(u => u.WarehouseId).Returns((Guid?)null);

        // Lookup repos — minimal stubs.
        var warehouseRepo = new Mock<IWarehouseRepository>();
        warehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo>
            {
                new(Guid.NewGuid(), "WH-MAIN", "Main"),
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

        var createValidator = new Mock<IValidator<AdjustmentCreateViewModel>>();
        createValidator.Setup(v => v.ValidateAsync(
                It.IsAny<AdjustmentCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var rejectValidator = new Mock<IValidator<RejectAdjustmentViewModel>>();
        rejectValidator.Setup(v => v.ValidateAsync(
                It.IsAny<RejectAdjustmentViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var ctrl = new AdjustmentsController(
            factory.Object, service.Object,
            warehouseFactory.Object, productFactory.Object,
            ownerFactory.Object, uomFactory.Object, locationFactory.Object,
            tenant.Object, currentUser.Object,
            createValidator.Object, rejectValidator.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, repo, service, createValidator, rejectValidator, currentUserId);
    }

    private static Adjustment SamplePending(Guid? requestedBy = null) => new()
    {
        Id = Guid.NewGuid(),
        AdjustmentNumber = "ADJ-X",
        Status = "Pending",
        RequestedBy = requestedBy ?? Guid.NewGuid(),
        QuantityDelta = 5m,
        Reason = "Found",
        LocationId = Guid.NewGuid(),
        ProductId = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        UomId = Guid.NewGuid(),
        WarehouseId = Guid.NewGuid(),
        RequestedAt = DateTime.UtcNow,
    };

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
                It.IsAny<AdjustmentFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<AdjustmentListRow>
            {
                Items = new(), Total = 0, Page = 1, PageSize = 20, TotalPages = 0,
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<AdjustmentFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdjustmentStatusCounts(All: 9, Pending: 4, Applied: 3, Rejected: 2));

        var json = Assert.IsType<JsonResult>(await b.Controller.GetData());
        var envelope = json.Value!;
        var counts = envelope.GetType().GetProperty("counts")!.GetValue(envelope)!;
        Assert.Equal(9, counts.GetType().GetProperty("all")!.GetValue(counts));
        Assert.Equal(4, counts.GetType().GetProperty("pending")!.GetValue(counts));
        Assert.Equal(3, counts.GetType().GetProperty("applied")!.GetValue(counts));
        Assert.Equal(2, counts.GetType().GetProperty("rejected")!.GetValue(counts));
    }

    [Fact]
    public async Task Create_Get_PopulatesLookups()
    {
        var b = BuildController();
        var result = await b.Controller.Create(default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdjustmentCreateViewModel>(view.Model);
        Assert.Single(vm.Warehouses);
        Assert.Single(vm.Products);
        Assert.Single(vm.Owners);
        Assert.Single(vm.Uoms);
    }

    [Fact]
    public async Task Create_Post_ValidationFails_ReturnsViewWithErrors()
    {
        var b = BuildController();
        b.CreateValidator.Setup(v => v.ValidateAsync(
                It.IsAny<AdjustmentCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Reason is required."),
            }));

        var result = await b.Controller.Create(new AdjustmentCreateViewModel(), default);
        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
        b.Service.Verify(s => s.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<CreateAdjustmentRequest>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_HappyPath_CallsService_AndRedirects()
    {
        var b = BuildController();
        var saved = SamplePending();
        b.Service.Setup(s => s.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<CreateAdjustmentRequest>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var vm = new AdjustmentCreateViewModel
        {
            WarehouseId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            UomId = Guid.NewGuid(),
            QuantityDelta = 3m,
            Reason = "Found",
        };

        var result = await b.Controller.Create(vm, default);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(b.Controller.Detail), redirect.ActionName);
        Assert.Equal(saved.Id, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Adjustment?)null);

        var result = await b.Controller.Detail(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_PendingByOtherUser_QuickActionsEnabled_NoSelfApprovalBanner()
    {
        var b = BuildController();
        var adj = SamplePending(requestedBy: Guid.NewGuid()); // different user
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), adj.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adj);

        var view = (ViewResult)await b.Controller.Detail(adj.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        var approve = vm.QuickActions.First(a => a.Label == "Approve & apply");
        var reject  = vm.QuickActions.First(a => a.Label == "Reject");
        Assert.True(approve.Enabled);
        Assert.True(reject.Enabled);
        Assert.True((bool)view.ViewData["CanDecide"]!);
        Assert.False((bool)view.ViewData["SelfApproval"]!);
    }

    [Fact]
    public async Task Detail_PendingByCurrentUser_QuickActionsDisabled_SelfApprovalBannerShows()
    {
        var b = BuildController();
        var adj = SamplePending(requestedBy: b.CurrentUserId); // self
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), adj.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adj);

        var view = (ViewResult)await b.Controller.Detail(adj.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.False(vm.QuickActions.First(a => a.Label == "Approve & apply").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Reject").Enabled);
        Assert.False((bool)view.ViewData["CanDecide"]!);
        Assert.True((bool)view.ViewData["SelfApproval"]!);
    }

    [Fact]
    public async Task Detail_AppliedAdjustment_QuickActionsDisabled()
    {
        var b = BuildController();
        var adj = SamplePending();
        adj.Status = "Applied";
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), adj.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adj);

        var view = (ViewResult)await b.Controller.Detail(adj.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.False(vm.QuickActions.First(a => a.Label == "Approve & apply").Enabled);
    }

    [Fact]
    public async Task Approve_HappyPath_CallsService_AndRedirects()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.ApproveAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Approve(id, default);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(b.Controller.Detail), redirect.ActionName);
        Assert.Contains("approved", (string)b.Controller.TempData["AdjustmentMessage"]!);
    }

    [Fact]
    public async Task Approve_ServiceThrowsInvalidOperation_SurfacesError()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.ApproveAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Self-approval blocked."));

        var result = await b.Controller.Approve(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Self-approval blocked.", b.Controller.TempData["AdjustmentError"]);
    }

    [Fact]
    public async Task Reject_HappyPath_CallsService_AndRedirects()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.RejectAsync(
                It.IsAny<Guid>(), id, "delta wrong", b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new RejectAdjustmentViewModel { Reason = "delta wrong" };
        var result = await b.Controller.Reject(id, vm, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Adjustment rejected.", b.Controller.TempData["AdjustmentMessage"]);
    }

    [Fact]
    public async Task Reject_ValidationFails_DoesNotCallService()
    {
        var b = BuildController();
        b.RejectValidator.Setup(v => v.ValidateAsync(
                It.IsAny<RejectAdjustmentViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Reason is required."),
            }));

        var result = await b.Controller.Reject(Guid.NewGuid(), new RejectAdjustmentViewModel(), default);
        Assert.IsType<RedirectToActionResult>(result);
        b.Service.Verify(s => s.RejectAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Reason is required.", b.Controller.TempData["AdjustmentError"]);
    }

    [Theory]
    [InlineData("Pending",  "pending",  "warning")]
    [InlineData("Applied",  "applied",  "success")]
    [InlineData("Rejected", "rejected", "neutral")]
    public void StatusMapper_RoundTrips(string db, string wire, string variant)
    {
        Assert.Equal(wire, WMS.Web.Services.Mappers.AdjustmentStatusMapper.ToWire(db));
        Assert.Equal(db, WMS.Web.Services.Mappers.AdjustmentStatusMapper.FromWire(wire));
        Assert.Equal(variant, WMS.Web.Services.Mappers.AdjustmentStatusMapper.ToBadgeVariant(db));
    }
}
