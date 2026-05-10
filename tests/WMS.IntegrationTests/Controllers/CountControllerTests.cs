using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Counts;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Counts;
using WMS.Domain.Entities.Counts;
using WMS.Web.Controllers;
using WMS.Web.Models.Counts;

namespace WMS.IntegrationTests.Controllers;

// Phase 21 — Mobile Cycle Count PWA controller tests. Mirrors Phase
// 18-20 shape (constructor injection + factory mocks). Constructor
// injection means submit happy paths ARE exercised end-to-end (no
// inline service-locator → no TD-041 family gap).
public class CountControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    private record Build(
        CountController Controller,
        Mock<ICycleCountService> Service,
        Mock<ICycleCountRepository> Repo,
        Guid CurrentUserId);

    private static Build BuildController(bool hasWarehouse = true)
    {
        var service = new Mock<ICycleCountService>();

        var repo = new Mock<ICycleCountRepository>();
        var factory = new Mock<ICycleCountRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);
        currentUser.SetupGet(u => u.WarehouseId).Returns(hasWarehouse ? WarehouseId : (Guid?)null);

        var ctrl = new CountController(
            service.Object, factory.Object, tenant.Object, currentUser.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, service, repo, currentUserId);
    }

    private static CycleCount NewSession(string status = "Counting") => new()
    {
        Id = Guid.NewGuid(),
        CountNumber = "CYC-20260511-0001",
        WarehouseId = WarehouseId,
        Status = status,
        StartedAt = DateTime.UtcNow.AddHours(-1),
    };

    private static CycleCountListRow NewListRow(string status = "Counting", string number = "CYC-20260511-0001") =>
        new(Id: Guid.NewGuid(),
            CountNumber: number,
            WarehouseId: WarehouseId,
            WarehouseCode: "WH-MAIN",
            LocationFilterCode: null,
            Status: status,
            LineCount: 24,
            CountedLineCount: 15,
            VarianceLineCount: 3,
            StartedByName: "Maya",
            StartedAt: DateTime.UtcNow.AddMinutes(-15));

    private static PagedResult<CycleCountListRow> Paged(params CycleCountListRow[] items) =>
        new()
        {
            Items = items.ToList(),
            Total = items.Length,
            Page = 1, PageSize = 50, TotalPages = 1,
        };

    // ================================================================
    // GET /count — queue
    // ================================================================

    [Fact]
    public async Task Index_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Index(CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
    }

    [Fact]
    public async Task Index_Happy_PartitionsCountingFromReview()
    {
        var b = BuildController();
        var counting = NewListRow("Counting", "CYC-A");
        var review   = NewListRow("Review",   "CYC-B");

        b.Repo.Setup(r => r.GetPagedAsync(
                It.Is<CycleCountFilter>(f => f.Status == "Counting"),
                It.IsAny<CancellationToken>())).ReturnsAsync(Paged(counting));
        b.Repo.Setup(r => r.GetPagedAsync(
                It.Is<CycleCountFilter>(f => f.Status == "Review"),
                It.IsAny<CancellationToken>())).ReturnsAsync(Paged(review));

        var result = await b.Controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var countingRows = Assert.IsAssignableFrom<IReadOnlyList<CycleCountListRow>>(view.Model);
        Assert.Single(countingRows);
        Assert.Equal("CYC-A", countingRows[0].CountNumber);
        var reviewRows = Assert.IsAssignableFrom<IReadOnlyList<CycleCountListRow>>(view.ViewData["ReviewRows"]);
        Assert.Single(reviewRows);
        Assert.Equal("CYC-B", reviewRows[0].CountNumber);
    }

    // ================================================================
    // GET /count/{sessionId} — task page
    // ================================================================

    [Fact]
    public async Task Task_NotFound_Returns404()
    {
        var b = BuildController();
        b.Service.Setup(s => s.GetByIdAsync(
                TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CycleCountDetail?)null);

        var result = await b.Controller.Task(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("Applied")]
    [InlineData("Cancelled")]
    public async Task Task_TerminalStatus_Returns404(string status)
    {
        var b = BuildController();
        var session = NewSession(status);
        b.Service.Setup(s => s.GetByIdAsync(
                TenantId, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(session, new List<CycleCountLine>()));

        var result = await b.Controller.Task(session.Id, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Task_Counting_ReturnsViewWithLineRows()
    {
        var b = BuildController();
        var session = NewSession("Counting");
        b.Service.Setup(s => s.GetByIdAsync(
                TenantId, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(session, new List<CycleCountLine>()));
        b.Repo.Setup(r => r.GetLineRowsByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CycleCountLineRow>
            {
                new(Id: Guid.NewGuid(), LineNumber: 1, StockId: Guid.NewGuid(),
                    ProductId: Guid.NewGuid(), ProductCode: "PROD-A001", ProductName: "Premium Widget",
                    LocationId: Guid.NewGuid(), LocationCode: "A-03-15-B",
                    UomCode: "EA", OwnerCode: "ACME", LotNumber: null, PalletNumber: null,
                    ExpectedQuantity: 50m, CountedQuantity: null, LineStatus: "Pending",
                    Notes: null),
            });

        var result = await b.Controller.Task(session.Id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<CycleCountLineRow>>(view.ViewData["LineRows"]);
        Assert.Single(rows);
        Assert.Equal("PROD-A001", rows[0].ProductCode);
    }

    [Fact]
    public async Task Task_Review_ReturnsViewWithLineRows()
    {
        // Review state should also render (read-only mode in the view).
        var b = BuildController();
        var session = NewSession("Review");
        b.Service.Setup(s => s.GetByIdAsync(
                TenantId, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(session, new List<CycleCountLine>()));
        b.Repo.Setup(r => r.GetLineRowsByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CycleCountLineRow>());

        var result = await b.Controller.Task(session.Id, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
    }

    // ================================================================
    // POST /count/save/{sessionId}
    // ================================================================

    [Fact]
    public async Task Save_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Save(
            Guid.NewGuid(), new MobileSaveCountViewModel(), CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
    }

    [Fact]
    public async Task Save_Happy_BouncesToTaskWithMessage()
    {
        var b = BuildController();
        var sessionId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        var vm = new MobileSaveCountViewModel
        {
            Lines = new List<CountLineEntry>
            {
                new() { LineId = lineId, CountedQuantity = 48m, LineStatus = "Counted" },
            },
        };

        var result = await b.Controller.Save(sessionId, vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("Saved 1", b.Controller.TempData["CountMessage"] as string);
        b.Service.Verify(s => s.SaveCountedQuantitiesAsync(
            TenantId, sessionId,
            It.Is<IReadOnlyList<CountLineUpdate>>(updates =>
                updates.Count == 1 && updates[0].LineId == lineId),
            b.CurrentUserId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Save_ServiceThrows_RedirectsToTaskWithError()
    {
        var b = BuildController();
        b.Service.Setup(s => s.SaveCountedQuantitiesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<CountLineUpdate>>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Session is not in Counting state."));

        var result = await b.Controller.Save(
            Guid.NewGuid(),
            new MobileSaveCountViewModel { Lines = new() { new() { LineId = Guid.NewGuid() } } },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("Counting", b.Controller.TempData["CountError"] as string);
    }

    // ================================================================
    // POST /count/submit/{sessionId}
    // ================================================================

    [Fact]
    public async Task Submit_Happy_SavesThenSubmitsAndBouncesToQueue()
    {
        var b = BuildController();
        var sessionId = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitForReviewAsync(
                TenantId, sessionId, b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new MobileSaveCountViewModel
        {
            Lines = new List<CountLineEntry>
            {
                new() { LineId = Guid.NewGuid(), CountedQuantity = 48m, LineStatus = "Counted" },
            },
        };

        var result = await b.Controller.Submit(sessionId, vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("Submitted for review", b.Controller.TempData["CountMessage"] as string);
        b.Service.Verify(s => s.SaveCountedQuantitiesAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<CountLineUpdate>>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_AlreadyReview_IdempotentMessage()
    {
        var b = BuildController();
        b.Service.Setup(s => s.SubmitForReviewAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await b.Controller.Submit(
            Guid.NewGuid(), new MobileSaveCountViewModel(), CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("already in Review", b.Controller.TempData["CountMessage"] as string);
    }

    [Fact]
    public async Task Submit_NoLines_SkipsSaveButCallsSubmit()
    {
        // Operator may submit a session with no draft edits in this
        // round (everything was saved earlier). Save call should NOT
        // fire when the request carries zero lines; SubmitForReview
        // should still fire.
        var b = BuildController();
        var sessionId = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitForReviewAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Submit(
            sessionId,
            new MobileSaveCountViewModel { Lines = new List<CountLineEntry>() },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        b.Service.Verify(s => s.SaveCountedQuantitiesAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<CountLineUpdate>>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        b.Service.Verify(s => s.SubmitForReviewAsync(
            TenantId, sessionId, b.CurrentUserId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // POST /count/cancel/{sessionId}
    // ================================================================

    [Fact]
    public async Task Cancel_BlankReason_RedirectsBackWithError_NoServiceCall()
    {
        var b = BuildController();
        var result = await b.Controller.Cancel(Guid.NewGuid(), "  ", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("required", b.Controller.TempData["CountError"] as string);
        b.Service.Verify(s => s.CancelAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Happy_BouncesToQueue()
    {
        var b = BuildController();
        var sessionId = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                TenantId, sessionId, "Wrong scope", b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Cancel(sessionId, "Wrong scope", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("cancelled", b.Controller.TempData["CountMessage"] as string);
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
        Assert.Contains("already cancelled", b.Controller.TempData["CountMessage"] as string);
    }
}
