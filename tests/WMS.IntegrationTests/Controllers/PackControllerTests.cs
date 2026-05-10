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

// Phase 19 — Mobile Pack PWA controller tests. Mirrors Phase 18
// ReceiveControllerTests shape (constructor injection + factory mocks).
//
// Path D from spec audit: per-line card pattern, no scan UI. Tests
// cover the queue / task page / submit guards / cancel reason gate.
// The /pack/submit happy path IS exercised here (unlike Phase 18
// receive's TD-041) because PackController has no inline service-
// locator — every dep is constructor-injected.
public class PackControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    private record Build(
        PackController Controller,
        Mock<IPackTaskRepository> PackRepo,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IProductRepository> ProductRepo,
        Mock<IBoxTypeRepository> BoxTypeRepo,
        Mock<IPackTaskService> Service,
        Guid CurrentUserId);

    private static Build BuildController(bool hasWarehouse = true)
    {
        var packRepo = new Mock<IPackTaskRepository>();
        var packFactory = new Mock<IPackTaskRepositoryFactory>();
        packFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(packRepo.Object);

        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var productRepo = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetMetaByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ProductLineMeta>());
        var productFactory = new Mock<IProductRepositoryFactory>();
        productFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(productRepo.Object);

        var boxTypeRepo = new Mock<IBoxTypeRepository>();
        boxTypeRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>());
        var boxTypeFactory = new Mock<IBoxTypeRepositoryFactory>();
        boxTypeFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(boxTypeRepo.Object);

        var service = new Mock<IPackTaskService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);
        currentUser.SetupGet(u => u.WarehouseId).Returns(hasWarehouse ? WarehouseId : (Guid?)null);

        var ctrl = new PackController(
            packFactory.Object, soFactory.Object, productFactory.Object,
            boxTypeFactory.Object, service.Object,
            tenant.Object, currentUser.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, packRepo, soRepo, productRepo, boxTypeRepo, service, currentUserId);
    }

    private static PackTask NewPackTask(string status = "Pending") =>
        new()
        {
            Id = Guid.NewGuid(),
            PackNumber = "PACK-20260510-0001",
            SalesOrderId = Guid.NewGuid(),
            Status = status,
            GeneratedAt = DateTime.UtcNow.AddMinutes(-5),
        };

    private static PackTaskLine NewPackLine(Guid taskId, Guid productId, decimal picked = 5m, int lineNumber = 1) =>
        new()
        {
            Id = Guid.NewGuid(),
            PackTaskId = taskId,
            LineNumber = lineNumber,
            SalesOrderLineId = Guid.NewGuid(),
            ProductId = productId,
            OwnerId = Guid.NewGuid(),
            UomId = Guid.NewGuid(),
            PickedQuantity = picked,
            LineStatus = "Pending",
        };

    private static SalesOrderDetail NewSoDetail(Guid soId, string soNumber = "SO-20260510-0001")
    {
        var so = new WMS.Domain.Entities.Outbound.SalesOrder
        {
            Id = soId,
            SoNumber = soNumber,
            Status = "Picked",
            CustomerId = Guid.NewGuid(),
            WarehouseId = WarehouseId,
        };
        return new SalesOrderDetail(so, new List<WMS.Domain.Entities.Outbound.SalesOrderLine>());
    }

    private static PackTaskListRow NewListRow(string number = "PACK-20260510-0001") =>
        new(Id: Guid.NewGuid(),
            PackNumber: number,
            SalesOrderId: Guid.NewGuid(),
            SoNumber: "SO-20260510-0001",
            CustomerCode: "ACME",
            CustomerName: "Acme Co",
            Status: "Pending",
            LineCount: 3,
            GeneratedAt: DateTime.UtcNow.AddMinutes(-10),
            GeneratedByName: "Maya",
            PackedAt: null,
            CancelledAt: null);

    // ================================================================
    // GET /pack — queue
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
    public async Task Index_Happy_ReturnsViewWithPendingTasks()
    {
        var b = BuildController();
        b.PackRepo.Setup(r => r.GetPagedAsync(
                It.Is<PackTaskFilter>(f => f.Status == "Pending"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PackTaskListRow>
            {
                Items = new List<PackTaskListRow> { NewListRow(), NewListRow("PACK-20260510-0002") },
                Total = 2,
                Page = 1,
                PageSize = 50,
                TotalPages = 1,
            });

        var result = await b.Controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<PackTaskListRow>>(view.Model);
        Assert.Equal(2, rows.Count);
    }

    // ================================================================
    // GET /pack/{taskId} — task page
    // ================================================================

    [Fact]
    public async Task Task_NotFound_Returns404()
    {
        var b = BuildController();
        b.PackRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackTaskDetail?)null);

        var result = await b.Controller.Task(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("Packed")]
    [InlineData("Cancelled")]
    public async Task Task_TerminalStatus_Returns404(string status)
    {
        var b = BuildController();
        var task = NewPackTask(status);
        b.PackRepo.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(task, new List<PackTaskLine>(), null));

        var result = await b.Controller.Task(task.Id, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Task_Happy_LoadsViewWithMetadata()
    {
        var b = BuildController();
        var task = NewPackTask();
        var productId = Guid.NewGuid();
        var detail = new PackTaskDetail(task,
            new List<PackTaskLine> { NewPackLine(task.Id, productId) }, null);

        b.PackRepo.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.SoRepo.Setup(r => r.GetByIdAsync(task.SalesOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewSoDetail(task.SalesOrderId, "SO-99-99"));
        b.ProductRepo.Setup(r => r.GetMetaByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ProductLineMeta>
            {
                [productId] = new("PROD-A001", "Premium Widget", "None"),
            });

        var result = await b.Controller.Task(task.Id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("SO-99-99", view.ViewData["SoNumber"]);
        var meta = Assert.IsAssignableFrom<IReadOnlyDictionary<Guid, ProductLineMeta>>(
            view.ViewData["ProductMeta"]);
        Assert.True(meta.ContainsKey(productId));
        Assert.Equal("PROD-A001", meta[productId].Code);
    }

    // ================================================================
    // POST /pack/submit/{taskId}
    // ================================================================

    [Fact]
    public async Task Submit_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Submit(
            Guid.NewGuid(),
            new SubmitPackTaskViewModel(),
            CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
    }

    [Fact]
    public async Task Submit_TaskNotFound_Returns404()
    {
        var b = BuildController();
        b.PackRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackTaskDetail?)null);

        var result = await b.Controller.Submit(
            Guid.NewGuid(),
            new SubmitPackTaskViewModel(),
            CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Submit_SerialTrackedLine_RejectedWithUseDesktopMessage()
    {
        var b = BuildController();
        var task = NewPackTask();
        var productId = Guid.NewGuid();
        var detail = new PackTaskDetail(task,
            new List<PackTaskLine> { NewPackLine(task.Id, productId) }, null);

        b.PackRepo.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.ProductRepo.Setup(r => r.GetMetaByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ProductLineMeta>
            {
                [productId] = new("PROD-S001", "Serial Widget", "LotAndSerial"),
            });

        var result = await b.Controller.Submit(
            task.Id,
            new SubmitPackTaskViewModel { Id = task.Id },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        var err = b.Controller.TempData["PackError"] as string;
        Assert.Contains("desktop", err);
        Assert.Contains("TD-043", err);

        b.Service.Verify(s => s.SubmitAsync(
            It.IsAny<Guid>(), It.IsAny<SubmitPackTaskRequest>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_Happy_BouncesToQueue()
    {
        var b = BuildController();
        var task = NewPackTask();
        var line = NewPackLine(task.Id, Guid.NewGuid());
        var detail = new PackTaskDetail(task, new List<PackTaskLine> { line }, null);

        b.PackRepo.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.Service.Setup(s => s.SubmitAsync(
                TenantId, It.IsAny<SubmitPackTaskRequest>(),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
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
            Id = task.Id,
            Lines = new List<PackedLineRow>
            {
                new() { LineId = line.Id, PackedQuantity = 5m, LineStatus = "Packed" },
            },
        };

        var result = await b.Controller.Submit(task.Id, vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        var msg = b.Controller.TempData["PackMessage"] as string;
        Assert.Contains("CTN-20260510-0001", msg);
        Assert.Contains("Packed", msg);
    }

    [Fact]
    public async Task Submit_ServiceThrows_RedirectsToTaskWithError()
    {
        var b = BuildController();
        var task = NewPackTask();
        var detail = new PackTaskDetail(task,
            new List<PackTaskLine> { NewPackLine(task.Id, Guid.NewGuid()) }, null);

        b.PackRepo.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), It.IsAny<SubmitPackTaskRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Pack task is not in Pending state."));

        var result = await b.Controller.Submit(
            task.Id, new SubmitPackTaskViewModel { Id = task.Id }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("Pending", b.Controller.TempData["PackError"] as string);
    }

    // ================================================================
    // POST /pack/cancel/{taskId}
    // ================================================================

    [Fact]
    public async Task Cancel_BlankReason_RedirectsToTaskWithError_NoServiceCall()
    {
        var b = BuildController();

        var result = await b.Controller.Cancel(Guid.NewGuid(), "  ", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("Cancel reason is required", b.Controller.TempData["PackError"] as string);
        b.Service.Verify(s => s.CancelAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_TooShortReason_Rejected()
    {
        var b = BuildController();
        var result = await b.Controller.Cancel(Guid.NewGuid(), "ab", CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(result);
        b.Service.Verify(s => s.CancelAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Happy_BouncesToQueue()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                TenantId, taskId, "Damaged in transit", b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Cancel(taskId, "Damaged in transit", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        var msg = b.Controller.TempData["PackMessage"] as string;
        Assert.Contains("cancelled", msg);
        Assert.Contains("SO state unchanged", msg);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_IdempotentMessage()
    {
        var b = BuildController();
        b.Service.Setup(s => s.CancelAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await b.Controller.Cancel(Guid.NewGuid(), "Already done", CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("already cancelled", b.Controller.TempData["PackMessage"] as string);
    }
}
