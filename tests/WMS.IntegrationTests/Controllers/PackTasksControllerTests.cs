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
using WMS.Domain.Entities.Outbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Outbound;

namespace WMS.IntegrationTests.Controllers;

// Phase 14D — PackTasksController tests. Same Build pattern as Phase
// 14C PickTasksControllerTests.
public class PackTasksControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        PackTasksController Controller,
        Mock<IPackTaskRepository> PackRepo,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IPackTaskService> Service,
        Mock<IValidator<CancelPackTaskViewModel>> CancelValidator,
        Guid CurrentUserId);

    private static Build BuildController()
    {
        var packRepo = new Mock<IPackTaskRepository>();
        var packFactory = new Mock<IPackTaskRepositoryFactory>();
        packFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(packRepo.Object);

        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var boxTypeRepo = new Mock<IBoxTypeRepository>();
        boxTypeRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(Guid.NewGuid(), "BOX-S", "Small box"),
            });
        var boxTypeFactory = new Mock<IBoxTypeRepositoryFactory>();
        boxTypeFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(boxTypeRepo.Object);

        var service = new Mock<IPackTaskService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);

        var cancelValidator = new Mock<IValidator<CancelPackTaskViewModel>>();
        cancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelPackTaskViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var ctrl = new PackTasksController(
            packFactory.Object, soFactory.Object, boxTypeFactory.Object, service.Object,
            tenant.Object, currentUser.Object, cancelValidator.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, packRepo, soRepo, service, cancelValidator, currentUserId);
    }

    private static PackTask NewHeader(Guid id, string status, Guid? soId = null) => new()
    {
        Id = id,
        PackNumber = "PACK-X",
        SalesOrderId = soId ?? Guid.NewGuid(),
        Status = status,
        GeneratedAt = DateTime.UtcNow,
    };

    private static PackTaskLine NewLine(Guid taskId, decimal picked = 5m) => new()
    {
        Id = Guid.NewGuid(),
        PackTaskId = taskId,
        SalesOrderLineId = Guid.NewGuid(),
        ProductId = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        UomId = Guid.NewGuid(),
        PickedQuantity = picked,
        LineStatus = "Pending",
    };

    // ================================================================
    // Detail
    // ================================================================

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.PackRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackTaskDetail?)null);

        var result = await b.Controller.Detail(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_PendingTask_CanSubmitAndCancel()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewHeader(taskId, "Pending"),
                new List<PackTaskLine> { NewLine(taskId) },
                Carton: null));

        var result = await b.Controller.Detail(taskId, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(true, viewResult.ViewData["IsPending"]);
        Assert.Equal(false, viewResult.ViewData["IsTerminal"]);
        Assert.Equal(true, viewResult.ViewData["CanSubmit"]);
        Assert.Equal(true, viewResult.ViewData["CanCancel"]);
    }

    [Theory]
    [InlineData("Packed")]
    [InlineData("Cancelled")]
    public async Task Detail_TerminalTask_CannotSubmitOrCancel(string status)
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewHeader(taskId, status),
                new List<PackTaskLine> { NewLine(taskId) },
                Carton: null));

        var result = await b.Controller.Detail(taskId, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(true, viewResult.ViewData["IsTerminal"]);
        Assert.Equal(false, viewResult.ViewData["CanSubmit"]);
        Assert.Equal(false, viewResult.ViewData["CanCancel"]);
    }

    // ================================================================
    // Submit
    // ================================================================

    [Fact]
    public async Task Submit_Happy_ProjectsRequest_RedirectsToDetail()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        b.Service.Setup(s => s.SubmitAsync(
                TenantId, It.IsAny<SubmitPackTaskRequest>(), b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskSubmissionResult(
                TaskStatus: "Packed",
                SalesOrderStatus: "Packed",
                FullyPackedLineCount: 1,
                ShortPackedLineCount: 0,
                SkippedLineCount: 0,
                TotalPackedQuantity: 5m,
                CartonNumber: "CTN-20260510-0001"));

        var vm = new SubmitPackTaskViewModel
        {
            Id = taskId,
            Lines = new()
            {
                new() { LineId = lineId, PackedQuantity = 5m, LineStatus = "Packed" },
            },
            BoxTypeId = null,
            WeightKg = 1.5m,
            CartonNotes = "first carton",
        };

        var result = await b.Controller.Submit(taskId, vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal(taskId, redirect.RouteValues!["id"]);

        b.Service.Verify(s => s.SubmitAsync(
            TenantId,
            It.Is<SubmitPackTaskRequest>(r => r.PackTaskId == taskId
                && r.Lines.Count == 1
                && r.Lines[0].LineId == lineId
                && r.Lines[0].PackedQuantity == 5m
                && r.WeightKg == 1.5m
                && r.CartonNotes == "first carton"),
            b.CurrentUserId,
            It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Contains("CTN-20260510-0001", b.Controller.TempData["PackTaskMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Submit_EmptyGuidBoxType_NormalisedToNull()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), It.IsAny<SubmitPackTaskRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskSubmissionResult(
                "Packed", "Packed", 0, 0, 0, 0m, "CTN-X"));

        var vm = new SubmitPackTaskViewModel
        {
            Id = taskId,
            Lines = new() { new() { LineId = Guid.NewGuid(), LineStatus = "Packed", PackedQuantity = 0m } },
            BoxTypeId = Guid.Empty,
            WeightKg = null,
            CartonNotes = null,
        };

        await b.Controller.Submit(taskId, vm, CancellationToken.None);

        b.Service.Verify(s => s.SubmitAsync(
            It.IsAny<Guid>(),
            It.Is<SubmitPackTaskRequest>(r => r.BoxTypeId == null),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_ServiceThrows_TempDataError_RedirectsToDetail()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), It.IsAny<SubmitPackTaskRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("missing line"));

        var vm = new SubmitPackTaskViewModel { Id = taskId, Lines = new() };
        var result = await b.Controller.Submit(taskId, vm, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("missing line", b.Controller.TempData["PackTaskError"]);
    }

    // ================================================================
    // Cancel
    // ================================================================

    [Fact]
    public async Task Cancel_ValidationFails_TempDataError_NoServiceCall()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.CancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelPackTaskViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Reason must be at least 3 characters."),
            }));

        var vm = new CancelPackTaskViewModel { Id = taskId, Reason = "x" };
        var result = await b.Controller.Cancel(taskId, vm, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("at least 3", b.Controller.TempData["PackTaskError"]?.ToString() ?? "");
        b.Service.Verify(s => s.CancelAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancel_Happy_CallsService_RouteIdWinsOverFormId()
    {
        var b = BuildController();
        var routeId = Guid.NewGuid();
        var formId = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                TenantId, routeId, "valid reason", b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new CancelPackTaskViewModel { Id = formId, Reason = "valid reason" };
        await b.Controller.Cancel(routeId, vm, CancellationToken.None);

        b.Service.Verify(s => s.CancelAsync(
            TenantId, routeId, "valid reason", b.CurrentUserId,
            It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Contains("cancelled", b.Controller.TempData["PackTaskMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_IdempotentMessage()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new CancelPackTaskViewModel { Id = taskId, Reason = "valid reason" };
        await b.Controller.Cancel(taskId, vm, CancellationToken.None);

        Assert.Contains("already cancelled",
            b.Controller.TempData["PackTaskMessage"]?.ToString() ?? "");
    }
}
