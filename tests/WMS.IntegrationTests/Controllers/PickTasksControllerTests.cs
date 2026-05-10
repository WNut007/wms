using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Outbound;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

// Phase 14C — PickTasksController tests. Same Build pattern as the
// Phase 12/13/14A controller test fixtures.
public class PickTasksControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        PickTasksController Controller,
        Mock<IPickTaskRepository> PickRepo,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IPickTaskService> Service,
        Mock<IValidator<CancelPickTaskViewModel>> CancelValidator,
        Guid CurrentUserId);

    private static Build BuildController()
    {
        var pickRepo = new Mock<IPickTaskRepository>();
        var pickFactory = new Mock<IPickTaskRepositoryFactory>();
        pickFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(pickRepo.Object);

        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var service = new Mock<IPickTaskService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);

        var cancelValidator = new Mock<IValidator<CancelPickTaskViewModel>>();
        cancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelPickTaskViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var ctrl = new PickTasksController(
            pickFactory.Object, soFactory.Object, service.Object,
            tenant.Object, currentUser.Object, cancelValidator.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, pickRepo, soRepo, service, cancelValidator, currentUserId);
    }

    private static PickTask NewHeader(Guid id, string status, Guid? soId = null) => new()
    {
        Id = id,
        PickNumber = "PICK-X",
        SalesOrderId = soId ?? Guid.NewGuid(),
        Status = status,
        GeneratedAt = DateTime.UtcNow,
    };

    private static PickTaskLine NewLine(Guid taskId, decimal expected = 5m) => new()
    {
        Id = Guid.NewGuid(),
        PickTaskId = taskId,
        OrderAllocationId = Guid.NewGuid(),
        StockId = Guid.NewGuid(),
        ProductId = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        UomId = Guid.NewGuid(),
        LocationId = Guid.NewGuid(),
        ExpectedQuantity = expected,
        LineStatus = "Pending",
    };

    // ================================================================
    // Detail
    // ================================================================

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.PickRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PickTaskDetail?)null);

        var result = await b.Controller.Detail(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_PendingTask_CanSubmitAndCancel()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewHeader(taskId, "Pending"),
                new List<PickTaskLine> { NewLine(taskId) }));

        var result = await b.Controller.Detail(taskId, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(true, viewResult.ViewData["IsPending"]);
        Assert.Equal(false, viewResult.ViewData["IsTerminal"]);
        Assert.Equal(true, viewResult.ViewData["CanSubmit"]);
        Assert.Equal(true, viewResult.ViewData["CanCancel"]);
    }

    [Theory]
    [InlineData("Picked")]
    [InlineData("PartiallyPicked")]
    [InlineData("Cancelled")]
    public async Task Detail_TerminalTask_CannotSubmitOrCancel(string status)
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewHeader(taskId, status),
                new List<PickTaskLine> { NewLine(taskId) }));

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
                TenantId, It.IsAny<SubmitPickTaskRequest>(), b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskSubmissionResult(
                TaskStatus: "Picked",
                SalesOrderStatus: "Picked",
                FullyPickedLineCount: 1,
                ShortPickedLineCount: 0,
                SkippedLineCount: 0,
                TotalPickedQuantity: 5m));

        var vm = new SubmitPickTaskViewModel
        {
            Id = taskId,
            Lines = new()
            {
                new() { LineId = lineId, PickedQuantity = 5m, LineStatus = "Picked" },
            },
        };

        var result = await b.Controller.Submit(taskId, vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal(taskId, redirect.RouteValues!["id"]);

        // Service called with route id (defence against tampered form Id).
        b.Service.Verify(s => s.SubmitAsync(
            TenantId,
            It.Is<SubmitPickTaskRequest>(r => r.PickTaskId == taskId
                && r.Lines.Count == 1
                && r.Lines[0].LineId == lineId
                && r.Lines[0].PickedQuantity == 5m
                && r.Lines[0].LineStatus == "Picked"),
            b.CurrentUserId,
            It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Contains("submitted", b.Controller.TempData["PickTaskMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Submit_ServiceThrows_TempDataError_RedirectsToDetail()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), It.IsAny<SubmitPickTaskRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("missing line"));

        var vm = new SubmitPickTaskViewModel { Id = taskId, Lines = new() };
        var result = await b.Controller.Submit(taskId, vm, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("missing line", b.Controller.TempData["PickTaskError"]);
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
                It.IsAny<CancelPickTaskViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Reason must be at least 3 characters."),
            }));

        var vm = new CancelPickTaskViewModel { Id = taskId, Reason = "x" };
        var result = await b.Controller.Cancel(taskId, vm, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("at least 3", b.Controller.TempData["PickTaskError"]?.ToString() ?? "");
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
        var formId = Guid.NewGuid();   // tampered — should be ignored
        b.Service.Setup(s => s.CancelAsync(
                TenantId, routeId, "valid reason", b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new CancelPickTaskViewModel { Id = formId, Reason = "valid reason" };
        var result = await b.Controller.Cancel(routeId, vm, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        b.Service.Verify(s => s.CancelAsync(
            TenantId, routeId, "valid reason", b.CurrentUserId,
            It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Contains("cancelled", b.Controller.TempData["PickTaskMessage"]?.ToString() ?? "");
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

        var vm = new CancelPickTaskViewModel { Id = taskId, Reason = "valid reason" };
        await b.Controller.Cancel(taskId, vm, CancellationToken.None);

        Assert.Contains("already cancelled",
            b.Controller.TempData["PickTaskMessage"]?.ToString() ?? "");
    }
}
