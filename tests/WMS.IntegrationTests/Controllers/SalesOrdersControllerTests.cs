using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Master;
using WMS.Domain.Entities.Outbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Outbound;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

// Phase 14A — SalesOrdersController tests. Same BuildController pattern
// as Adjustments / CycleCounts / Transfers.
public class SalesOrdersControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        SalesOrdersController Controller,
        Mock<ISalesOrderRepository> Repo,
        Mock<ISalesOrderService> Service,
        Mock<IAllocationService> AllocationService,
        Mock<IPickTaskService> PickTaskService,
        Mock<ICustomerRepository> CustomerRepo,
        Mock<IValidator<SalesOrderCreateViewModel>> CreateValidator,
        Mock<IValidator<SalesOrderEditViewModel>> EditValidator,
        Guid CurrentUserId);

    private static Build BuildController()
    {
        var repo = new Mock<ISalesOrderRepository>();
        var factory = new Mock<ISalesOrderRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<SalesOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderStatusCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        repo.Setup(r => r.GetLineRowsByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SalesOrderLineRow>());

        var service = new Mock<ISalesOrderService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);
        currentUser.SetupGet(u => u.WarehouseId).Returns((Guid?)null);

        var customerRepo = new Mock<ICustomerRepository>();
        customerRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem> { new(Guid.NewGuid(), "CUST-1", "Customer 1") });
        customerRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Customer
            {
                Id = id, Code = "CUST-X", Name = "Customer X", Status = "Active",
            });
        var customerFactory = new Mock<ICustomerRepositoryFactory>();
        customerFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(customerRepo.Object);

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

        var createValidator = new Mock<IValidator<SalesOrderCreateViewModel>>();
        createValidator.Setup(v => v.ValidateAsync(
                It.IsAny<SalesOrderCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var editValidator = new Mock<IValidator<SalesOrderEditViewModel>>();
        editValidator.Setup(v => v.ValidateAsync(
                It.IsAny<SalesOrderEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        // Phase 14B added IAllocationService + IOrderAllocationRepository-
        // Factory deps. Default-mocked internally per
        // feedback_test_build_helper_pattern.md so existing test sites
        // (Build record tuple) stay stable.
        var allocationService = new Mock<IAllocationService>();
        var allocRepo = new Mock<IOrderAllocationRepository>();
        allocRepo.Setup(r => r.GetActiveBySalesOrderIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderAllocationRow>());
        var allocFactory = new Mock<IOrderAllocationRepositoryFactory>();
        allocFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(allocRepo.Object);

        // Phase 14C added IPickTaskService dep (Generate POST endpoint).
        // Default-mocked internally — same convention as 14B above.
        var pickTaskService = new Mock<IPickTaskService>();

        var ctrl = new SalesOrdersController(
            factory.Object, service.Object,
            allocationService.Object, pickTaskService.Object, allocFactory.Object,
            customerFactory.Object, warehouseFactory.Object,
            productFactory.Object, ownerFactory.Object, uomFactory.Object,
            tenant.Object, currentUser.Object,
            createValidator.Object, editValidator.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, repo, service, allocationService, pickTaskService,
            customerRepo, createValidator, editValidator, currentUserId);
    }

    private static SalesOrder SampleHeader(string status = "Draft") => new()
    {
        Id = Guid.NewGuid(),
        SoNumber = "SO-X",
        CustomerId = Guid.NewGuid(),
        WarehouseId = Guid.NewGuid(),
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Status = status,
        CreatedAt = DateTime.UtcNow,
    };

    private static SalesOrderDetail SampleDetail(string status = "Draft") =>
        new(SampleHeader(status), new List<SalesOrderLine>());

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
                It.IsAny<SalesOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<SalesOrderListRow>
            {
                Items = new(), Total = 0, Page = 1, PageSize = 20, TotalPages = 0,
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<SalesOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderStatusCounts(
                All: 7, Draft: 2, Open: 4, Allocating: 0, Allocated: 0,
                Picking: 0, Picked: 0, PartiallyPicked: 0, Packed: 0, Cancelled: 1));

        var json = Assert.IsType<JsonResult>(await b.Controller.GetData());
        var envelope = json.Value!;
        var counts = envelope.GetType().GetProperty("counts")!.GetValue(envelope)!;
        Assert.Equal(7, counts.GetType().GetProperty("all")!.GetValue(counts));
        Assert.Equal(2, counts.GetType().GetProperty("draft")!.GetValue(counts));
        Assert.Equal(4, counts.GetType().GetProperty("open")!.GetValue(counts));
        Assert.Equal(1, counts.GetType().GetProperty("cancelled")!.GetValue(counts));
    }

    [Fact]
    public async Task Create_Get_PopulatesLookups()
    {
        var b = BuildController();
        var view = Assert.IsType<ViewResult>(await b.Controller.Create(default));
        var vm = Assert.IsType<SalesOrderCreateViewModel>(view.Model);
        Assert.Single(vm.Customers);
        Assert.Single(vm.Warehouses);
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
                It.IsAny<SalesOrderCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("CustomerId", "Customer is required."),
            }));

        var result = await b.Controller.Create(new SalesOrderCreateViewModel(), default);
        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
        b.Service.Verify(s => s.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<CreateSalesOrderRequest>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_HappyPath_RedirectsToDetail()
    {
        var b = BuildController();
        var saved = SampleDetail();
        b.Service.Setup(s => s.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<CreateSalesOrderRequest>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var vm = new SalesOrderCreateViewModel
        {
            CustomerId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            OrderDate = DateTime.UtcNow.Date,
            Lines = new()
            {
                new() { LineNumber = 1, ProductId = Guid.NewGuid(),
                        OwnerId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                        OrderedQuantity = 5m },
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
            .ReturnsAsync((SalesOrderDetail?)null);

        Assert.IsType<NotFoundResult>(
            await b.Controller.Detail(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Detail_Draft_AllActionsExceptSubmit_AreEnabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Draft");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Edit").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Submit").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Cancel").Enabled);
    }

    [Fact]
    public async Task Detail_Open_SubmitDisabled_EditAndCancelEnabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Open");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Edit").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Submit").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Cancel").Enabled);
    }

    [Fact]
    public async Task Detail_Cancelled_AllActionsDisabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Cancelled");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.All(vm.QuickActions, qa => Assert.False(qa.Enabled));
    }

    [Fact]
    public async Task Edit_Get_OnCancelled_RedirectsToDetailWithError()
    {
        var b = BuildController();
        var detail = SampleDetail("Cancelled");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await b.Controller.Edit(detail.Header.Id, default);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(b.Controller.Detail), redirect.ActionName);
        Assert.Contains("cancelled", (string)b.Controller.TempData["SalesOrderError"]!);
    }

    [Fact]
    public async Task Edit_Get_OnOpen_LinesLockedFlagSet()
    {
        var b = BuildController();
        var detail = SampleDetail("Open");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Edit(detail.Header.Id, default);
        var vm = Assert.IsType<SalesOrderEditViewModel>(view.Model);
        Assert.True(vm.LinesLocked);
    }

    [Fact]
    public async Task Edit_Get_OnDraft_LinesNotLocked()
    {
        var b = BuildController();
        var detail = SampleDetail("Draft");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Edit(detail.Header.Id, default);
        var vm = Assert.IsType<SalesOrderEditViewModel>(view.Model);
        Assert.False(vm.LinesLocked);
    }

    [Fact]
    public async Task Edit_Post_HappyPath_RedirectsToDetail()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        var saved = SampleDetail();
        b.Service.Setup(s => s.UpdateAsync(
                It.IsAny<Guid>(), id, It.IsAny<UpdateSalesOrderRequest>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var vm = new SalesOrderEditViewModel
        {
            Id = id, OrderDate = DateTime.UtcNow.Date,
            ReplaceLines = false,
        };

        var result = await b.Controller.Edit(id, vm, default);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(b.Controller.Detail), redirect.ActionName);
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
        Assert.Contains("submitted", (string)b.Controller.TempData["SalesOrderMessage"]!);
    }

    [Fact]
    public async Task Submit_ServiceThrows_SurfacesError()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cannot submit zero-line SO."));

        var result = await b.Controller.Submit(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Cannot submit zero-line SO.", b.Controller.TempData["SalesOrderError"]);
    }

    [Fact]
    public async Task Cancel_HappyPath_RedirectsWithMessage()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Cancel(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("cancelled", (string)b.Controller.TempData["SalesOrderMessage"]!);
    }

    [Theory]
    [InlineData("Draft",      "draft",      "neutral")]
    [InlineData("Open",       "open",       "success")]
    [InlineData("Allocating", "allocating", "warning")]
    [InlineData("Allocated",  "allocated",  "info")]
    [InlineData("Cancelled",  "cancelled",  "neutral")]
    public void StatusMapper_RoundTrips(string db, string wire, string variant)
    {
        Assert.Equal(wire, WMS.Web.Services.Mappers.SalesOrderStatusMapper.ToWire(db));
        Assert.Equal(db, WMS.Web.Services.Mappers.SalesOrderStatusMapper.FromWire(wire));
        Assert.Equal(variant, WMS.Web.Services.Mappers.SalesOrderStatusMapper.ToBadgeVariant(db));
    }

    // ================================================================
    // Phase 14B — Allocate endpoint + state-gating across 5 statuses
    // ================================================================

    [Fact]
    public async Task Detail_Open_AllocateEnabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Open");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Allocate").Enabled);
    }

    [Fact]
    public async Task Detail_Allocating_AllocateAndCancelEnabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Allocating");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Allocate").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Cancel").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Submit").Enabled);
    }

    [Fact]
    public async Task Detail_Allocated_AllocateDisabled_CancelEnabled()
    {
        var b = BuildController();
        var detail = SampleDetail("Allocated");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var view = (ViewResult)await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.False(vm.QuickActions.First(a => a.Label == "Allocate").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Cancel").Enabled);
    }

    [Fact]
    public async Task Detail_GetData_ReturnsExpandedCountsShape()
    {
        var b = BuildController();
        b.Repo.Setup(r => r.GetPagedAsync(
                It.IsAny<SalesOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<SalesOrderListRow>
            {
                Items = new(), Total = 0, Page = 1, PageSize = 20, TotalPages = 0,
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<SalesOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderStatusCounts(
                All: 10, Draft: 1, Open: 2, Allocating: 3, Allocated: 3,
                Picking: 0, Picked: 0, PartiallyPicked: 0, Packed: 0, Cancelled: 1));

        var json = Assert.IsType<JsonResult>(await b.Controller.GetData());
        var counts = json.Value!.GetType().GetProperty("counts")!.GetValue(json.Value)!;
        Assert.Equal(3, counts.GetType().GetProperty("allocating")!.GetValue(counts));
        Assert.Equal(3, counts.GetType().GetProperty("allocated")!.GetValue(counts));
    }

    [Fact]
    public async Task Allocate_HappyFull_RedirectsWithFullMessage()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.AllocationService.Setup(s => s.AllocateAsync(
                It.IsAny<Guid>(), id, null, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AllocationResult(
                IsFullyAllocated: true,
                LineCount: 3,
                FullyAllocatedLineCount: 3,
                ShortfallByLineId: new Dictionary<Guid, decimal>()));

        var result = await b.Controller.Allocate(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("Fully allocated", (string)b.Controller.TempData["SalesOrderMessage"]!);
    }

    [Fact]
    public async Task Allocate_HappyPartial_RedirectsWithPartialMessage()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.AllocationService.Setup(s => s.AllocateAsync(
                It.IsAny<Guid>(), id, null, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AllocationResult(
                IsFullyAllocated: false,
                LineCount: 5,
                FullyAllocatedLineCount: 2,
                ShortfallByLineId: new Dictionary<Guid, decimal>()));

        var result = await b.Controller.Allocate(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        var msg = (string)b.Controller.TempData["SalesOrderMessage"]!;
        Assert.Contains("Partially", msg);
        Assert.Contains("2 of 5", msg);
    }

    [Fact]
    public async Task Allocate_ServiceThrows_SurfacesError()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.AllocationService.Setup(s => s.AllocateAsync(
                It.IsAny<Guid>(), id, null, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Wrong state"));

        var result = await b.Controller.Allocate(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Wrong state", b.Controller.TempData["SalesOrderError"]);
    }

    // ================================================================
    // Generate (Phase 14C)
    // ================================================================

    [Fact]
    public async Task Generate_Happy_RedirectsToPickTaskDetail_WithTempDataMessage()
    {
        var b = BuildController();
        var soId = Guid.NewGuid();
        var newPickId = Guid.NewGuid();
        b.PickTaskService.Setup(s => s.GenerateAsync(
                TenantId, soId, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskGenerationResult(
                PickTaskId: newPickId,
                PickNumber: "PICK-20260510-0001",
                LineCount: 3,
                TotalExpectedQuantity: 42m));

        var result = await b.Controller.Generate(soId, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal("PickTasks", redirect.ControllerName);
        Assert.Equal(newPickId, redirect.RouteValues!["id"]);

        var msg = b.Controller.TempData["PickTaskMessage"] as string;
        Assert.NotNull(msg);
        Assert.Contains("PICK-20260510-0001", msg);
        Assert.Contains("3 line", msg);
    }

    [Fact]
    public async Task Generate_ServiceThrows_RedirectsToSoDetail_WithError()
    {
        var b = BuildController();
        var soId = Guid.NewGuid();
        b.PickTaskService.Setup(s => s.GenerateAsync(
                It.IsAny<Guid>(), soId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SO not in Allocated state"));

        var result = await b.Controller.Generate(soId, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Null(redirect.ControllerName);   // same controller (SalesOrders)
        Assert.Equal(soId, redirect.RouteValues!["id"]);
        Assert.Equal("SO not in Allocated state", b.Controller.TempData["SalesOrderError"]);
    }
}
