using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Outbound;

namespace WMS.IntegrationTests.Controllers;

// Phase 16 — Mobile Pick PWA. PickController is the desktop
// PickTasksController's mobile sibling — same IPickTaskService entry
// points, different surfaces (queue + per-task page).
public class PickControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    private record Build(
        PickController Controller,
        Mock<IPickTaskRepository> PickRepo,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IPickTaskService> Service,
        Guid CurrentUserId);

    // hasWarehouse: when false, the WarehouseId guard should redirect
    // to /Auth/SelectWarehouse.
    private static Build BuildController(bool hasWarehouse = true)
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
        currentUser.SetupGet(u => u.WarehouseId).Returns(hasWarehouse ? WarehouseId : (Guid?)null);

        var ctrl = new PickController(
            pickFactory.Object, soFactory.Object, service.Object,
            tenant.Object, currentUser.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, pickRepo, soRepo, service, currentUserId);
    }

    // ================================================================
    // GET /pick — queue
    // ================================================================

    [Fact]
    public async Task Index_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Index(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
        Assert.Equal("Auth", redirect.ControllerName);
    }

    [Fact]
    public async Task Index_Happy_MergesInProgressThenPending()
    {
        var b = BuildController();
        var inProgressId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();

        b.PickRepo.Setup(r => r.GetPagedAsync(
                It.Is<PickTaskFilter>(f => f.Status == "Pending"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PickTaskListRow>
            {
                Items = new List<PickTaskListRow>
                {
                    new(pendingId, "PICK-002", Guid.NewGuid(), "SO-002",
                        "CUST-B", "Cust B", "Pending", 2,
                        DateTime.UtcNow.AddMinutes(-1), "Maya", null, null),
                },
                Total = 1, Page = 1, PageSize = 50, TotalPages = 1,
            });
        b.PickRepo.Setup(r => r.GetPagedAsync(
                It.Is<PickTaskFilter>(f => f.Status == "InProgress"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PickTaskListRow>
            {
                Items = new List<PickTaskListRow>
                {
                    new(inProgressId, "PICK-001", Guid.NewGuid(), "SO-001",
                        "CUST-A", "Cust A", "InProgress", 3,
                        DateTime.UtcNow.AddMinutes(-5), "Maya", null, null),
                },
                Total = 1, Page = 1, PageSize = 50, TotalPages = 1,
            });

        var result = await b.Controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<PickTaskListRow>>(view.Model!);
        Assert.Equal(2, rows.Count);
        // InProgress first (returning operator continues their task),
        // Pending below.
        Assert.Equal(inProgressId, rows[0].Id);
        Assert.Equal(pendingId,    rows[1].Id);
    }

    // ================================================================
    // GET /pick/{id} — task page
    // ================================================================

    [Fact]
    public async Task Task_NotFound_Returns404()
    {
        var b = BuildController();
        b.PickRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PickTaskDetail?)null);

        var result = await b.Controller.Task(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Task_Happy_ReturnsViewWithSoNumber()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        var soId = Guid.NewGuid();

        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                new PickTask
                {
                    Id = taskId, PickNumber = "PICK-X",
                    SalesOrderId = soId, Status = "Pending",
                    GeneratedAt = DateTime.UtcNow,
                },
                new List<PickTaskLine>()));
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                new SalesOrder
                {
                    Id = soId, SoNumber = "SO-X",
                    WarehouseId = WarehouseId,
                    Status = "Picking",
                    OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
                },
                new List<SalesOrderLine>()));

        var result = await b.Controller.Task(taskId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("SO-X", view.ViewData["SoNumber"]);
    }

    // ================================================================
    // POST /pick/submit/{id}
    // ================================================================

    [Fact]
    public async Task Submit_Happy_RedirectsToQueue()
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

        // Mobile bounces back to queue (not Detail) — operator grabs
        // next task instead of staring at terminal page.
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("Submitted", b.Controller.TempData["PickMessage"]?.ToString() ?? "");

        // Service called with route id (defence against tampered form).
        b.Service.Verify(s => s.SubmitAsync(
            TenantId,
            It.Is<SubmitPickTaskRequest>(r => r.PickTaskId == taskId
                && r.Lines.Count == 1
                && r.Lines[0].LineId == lineId),
            b.CurrentUserId,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_ServiceThrows_RedirectsToTaskWithError()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), It.IsAny<SubmitPickTaskRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("missing line"));

        var vm = new SubmitPickTaskViewModel { Id = taskId, Lines = new() };
        var result = await b.Controller.Submit(taskId, vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Equal(taskId, redirect.RouteValues!["id"]);
        Assert.Equal("missing line", b.Controller.TempData["PickError"]);
    }

    // ================================================================
    // POST /pick/cancel/{id}
    // ================================================================

    [Fact]
    public async Task Cancel_BlankReason_RedirectsBackWithError_NoServiceCall()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();

        var result = await b.Controller.Cancel(taskId, "  ", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("3+ characters", b.Controller.TempData["PickError"]?.ToString() ?? "");
        b.Service.Verify(s => s.CancelAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancel_Happy_CallsService_RedirectsToQueue()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                TenantId, taskId, "needed reassignment", b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Cancel(taskId, "needed reassignment", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("cancelled", b.Controller.TempData["PickMessage"]?.ToString() ?? "");
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

        await b.Controller.Cancel(taskId, "valid reason", CancellationToken.None);

        Assert.Contains("already cancelled",
            b.Controller.TempData["PickMessage"]?.ToString() ?? "");
    }
}
