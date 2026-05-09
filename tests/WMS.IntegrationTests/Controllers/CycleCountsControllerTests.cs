using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Counts;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Counts;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Counts;
using WMS.Web.Controllers;
using WMS.Web.Models.Counts;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

// Phase 12 — CycleCountsController tests. Mirrors the Phase 11A
// AdjustmentsController test pattern.
public class CycleCountsControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        CycleCountsController Controller,
        Mock<ICycleCountRepository> Repo,
        Mock<ICycleCountService> Service,
        Mock<IValidator<CycleCountCreateViewModel>> CreateValidator,
        Mock<IValidator<CancelCycleCountViewModel>> CancelValidator,
        Guid CurrentUserId);

    private static Build BuildController()
    {
        var repo = new Mock<ICycleCountRepository>();
        var factory = new Mock<ICycleCountRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<CycleCountFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountStatusCounts(0, 0, 0, 0, 0));
        repo.Setup(r => r.GetLineRowsByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CycleCountLineRow>());

        var service = new Mock<ICycleCountService>();

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
            });
        var warehouseFactory = new Mock<IWarehouseRepositoryFactory>();
        warehouseFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(warehouseRepo.Object);

        var locationRepo = new Mock<ILocationRepository>();
        locationRepo.Setup(r => r.GetActiveByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem> { new(Guid.NewGuid(), "BIN-A1", "Bin A1") });
        var locationFactory = new Mock<ILocationRepositoryFactory>();
        locationFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(locationRepo.Object);

        var createValidator = new Mock<IValidator<CycleCountCreateViewModel>>();
        createValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CycleCountCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var cancelValidator = new Mock<IValidator<CancelCycleCountViewModel>>();
        cancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelCycleCountViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var ctrl = new CycleCountsController(
            factory.Object, service.Object,
            warehouseFactory.Object, locationFactory.Object,
            tenant.Object, currentUser.Object,
            createValidator.Object, cancelValidator.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, repo, service, createValidator, cancelValidator, currentUserId);
    }

    private static CycleCount NewHeader(string status = "Counting", Guid? countedBy = null) => new()
    {
        Id = Guid.NewGuid(),
        CountNumber = "CYC-X",
        WarehouseId = Guid.NewGuid(),
        Status = status,
        StartedBy = Guid.NewGuid(),
        StartedAt = DateTime.UtcNow,
        CountedBy = countedBy,
    };

    [Fact]
    public void Index_ReturnsView()
    {
        var b = BuildController();
        Assert.IsType<ViewResult>(b.Controller.Index());
    }

    [Fact]
    public async Task GetData_ReturnsJsonWithCounts()
    {
        var b = BuildController();
        b.Repo.Setup(r => r.GetPagedAsync(
                It.IsAny<CycleCountFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CycleCountListRow>
            {
                Items = new(), Total = 0, Page = 1, PageSize = 20, TotalPages = 0,
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<CycleCountFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountStatusCounts(15, 4, 5, 5, 1));

        var json = Assert.IsType<JsonResult>(await b.Controller.GetData());
        var envelope = json.Value!;
        var counts = envelope.GetType().GetProperty("counts")!.GetValue(envelope)!;
        Assert.Equal(15, counts.GetType().GetProperty("all")!.GetValue(counts));
        Assert.Equal(4,  counts.GetType().GetProperty("counting")!.GetValue(counts));
        Assert.Equal(5,  counts.GetType().GetProperty("review")!.GetValue(counts));
        Assert.Equal(5,  counts.GetType().GetProperty("applied")!.GetValue(counts));
        Assert.Equal(1,  counts.GetType().GetProperty("cancelled")!.GetValue(counts));
    }

    [Fact]
    public async Task Create_Get_PopulatesLookups()
    {
        var b = BuildController();
        var view = Assert.IsType<ViewResult>(await b.Controller.Create(default));
        var vm = Assert.IsType<CycleCountCreateViewModel>(view.Model);
        Assert.Single(vm.Warehouses);
    }

    [Fact]
    public async Task Create_Post_HappyPath_RedirectsToDetail()
    {
        var b = BuildController();
        var saved = NewHeader();
        var detail = new CycleCountDetail(saved, Array.Empty<CycleCountLine>());
        b.Service.Setup(s => s.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<CreateCycleCountRequest>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var vm = new CycleCountCreateViewModel { WarehouseId = Guid.NewGuid() };
        var result = await b.Controller.Create(vm, default);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(b.Controller.Detail), redirect.ActionName);
        Assert.Equal(saved.Id, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task Create_Post_ServiceThrowsEmptySnapshot_ReturnsViewWithError()
    {
        var b = BuildController();
        b.Service.Setup(s => s.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<CreateCycleCountRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No positive-OnHand stock at scope."));

        var vm = new CycleCountCreateViewModel { WarehouseId = Guid.NewGuid() };
        var result = await b.Controller.Create(vm, default);
        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CycleCountDetail?)null);

        var result = await b.Controller.Detail(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_Counting_SubmitEnabled_ApplyDisabled()
    {
        var b = BuildController();
        var header = NewHeader("Counting");
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(header, Array.Empty<CycleCountLine>()));

        var view = (ViewResult)await b.Controller.Detail(header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.True(vm.QuickActions.First(a => a.Label == "Submit for review").Enabled);
        Assert.False(vm.QuickActions.First(a => a.Label == "Apply").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Cancel").Enabled);
        Assert.True((bool)view.ViewData["IsCounting"]!);
    }

    [Fact]
    public async Task Detail_ReviewByOtherUser_ApplyEnabled()
    {
        var b = BuildController();
        var counterId = Guid.NewGuid();  // different from currentUser
        var header = NewHeader("Review", countedBy: counterId);
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(header, Array.Empty<CycleCountLine>()));

        var view = (ViewResult)await b.Controller.Detail(header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.False(vm.QuickActions.First(a => a.Label == "Submit for review").Enabled);
        Assert.True(vm.QuickActions.First(a => a.Label == "Apply").Enabled);
        Assert.True((bool)view.ViewData["CanApply"]!);
        Assert.False((bool)view.ViewData["SelfApproval"]!);
    }

    [Fact]
    public async Task Detail_ReviewByCurrentUser_ApplyDisabled_SelfApprovalBanner()
    {
        var b = BuildController();
        var header = NewHeader("Review", countedBy: b.CurrentUserId);  // self
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(header, Array.Empty<CycleCountLine>()));

        var view = (ViewResult)await b.Controller.Detail(header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.False(vm.QuickActions.First(a => a.Label == "Apply").Enabled);
        Assert.True((bool)view.ViewData["SelfApproval"]!);
    }

    [Fact]
    public async Task Detail_Applied_AllDecisionActionsDisabled()
    {
        var b = BuildController();
        var header = NewHeader("Applied", countedBy: Guid.NewGuid());
        header.AppliedAt = DateTime.UtcNow;
        header.ReviewedAt = DateTime.UtcNow;
        header.ReviewedBy = Guid.NewGuid();
        header.CountedAt = DateTime.UtcNow;
        b.Service.Setup(s => s.GetByIdAsync(
                It.IsAny<Guid>(), header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(header, Array.Empty<CycleCountLine>()));

        var view = (ViewResult)await b.Controller.Detail(header.Id, default);
        var vm = Assert.IsType<DetailPageViewModel>(view.Model);

        Assert.All(vm.QuickActions, a => Assert.False(a.Enabled));
    }

    [Fact]
    public async Task Submit_HappyPath_CallsService_AndRedirects()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitForReviewAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Submit(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("submitted", (string)b.Controller.TempData["CycleCountMessage"]!);
    }

    [Fact]
    public async Task Apply_HappyPath_CallsService_AndRedirects()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.ApproveAndApplyAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Apply(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("applied", (string)b.Controller.TempData["CycleCountMessage"]!);
    }

    [Fact]
    public async Task Apply_ServiceThrowsSelfApproval_SurfacesError()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.ApproveAndApplyAsync(
                It.IsAny<Guid>(), id, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Self-approval blocked."));

        var result = await b.Controller.Apply(id, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Self-approval blocked.", b.Controller.TempData["CycleCountError"]);
    }

    [Fact]
    public async Task Cancel_HappyPath_CallsService()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                It.IsAny<Guid>(), id, "abandon", b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Cancel(
            id, new CancelCycleCountViewModel { Reason = "abandon" }, default);
        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Cancel_ValidationFails_DoesNotCallService()
    {
        var b = BuildController();
        b.CancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelCycleCountViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Reason is required."),
            }));

        var result = await b.Controller.Cancel(
            Guid.NewGuid(), new CancelCycleCountViewModel(), default);
        Assert.IsType<RedirectToActionResult>(result);
        b.Service.Verify(s => s.CancelAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveCounts_HappyPath_CallsService()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        IReadOnlyList<CountLineUpdate>? captured = null;
        b.Service.Setup(s => s.SaveCountedQuantitiesAsync(
                It.IsAny<Guid>(), id, It.IsAny<IReadOnlyList<CountLineUpdate>>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, IReadOnlyList<CountLineUpdate>, Guid, CancellationToken>(
                (_, _, l, _, _) => captured = l)
            .Returns(Task.CompletedTask);

        var vm = new SaveCountsViewModel
        {
            Lines = new()
            {
                new() { LineId = lineId, CountedQuantity = 7m, LineStatus = "Counted", Notes = "ok" },
            },
        };

        var result = await b.Controller.SaveCounts(id, vm, default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(captured);
        Assert.Single(captured!);
        Assert.Equal(7m, captured[0].CountedQuantity);
        Assert.Equal("Counted", captured[0].LineStatus);
    }

    [Theory]
    [InlineData("Counting",  "counting",  "warning")]
    [InlineData("Review",    "review",    "info")]
    [InlineData("Applied",   "applied",   "success")]
    [InlineData("Cancelled", "cancelled", "neutral")]
    public void StatusMapper_RoundTrips(string db, string wire, string variant)
    {
        Assert.Equal(wire, WMS.Web.Services.Mappers.CycleCountStatusMapper.ToWire(db));
        Assert.Equal(db,   WMS.Web.Services.Mappers.CycleCountStatusMapper.FromWire(wire));
        Assert.Equal(variant, WMS.Web.Services.Mappers.CycleCountStatusMapper.ToBadgeVariant(db));
    }
}
