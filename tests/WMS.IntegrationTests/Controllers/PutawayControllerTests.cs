using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Inbound;
using WMS.Common.Auth;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;
using WMS.Web.Controllers;
using WMS.Web.Models.Inbound;

namespace WMS.IntegrationTests.Controllers;

// Phase 20 — Mobile Putaway PWA controller tests. Mirrors Phase 18+19
// shape (constructor injection + factory mocks). Replaces the Phase 1
// PutawayController which had no tests.
//
// Submit's override-code resolution path uses ITenantConnectionFactory
// + inline Dapper which can't be cleanly mocked without a service-
// provider fixture (same TD-041 family as Phase 18 ReceiveController).
// Submit's suggestion-fallback path IS exercised end-to-end (no inline
// service-locator on that path).
public class PutawayControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    private record Build(
        PutawayController Controller,
        Mock<IPutawayService> Service,
        Mock<IStockRepository> StockRepo,
        Mock<ITenantConnectionFactory> TenantConn,
        Guid CurrentUserId);

    private static Build BuildController(bool hasWarehouse = true)
    {
        var service = new Mock<IPutawayService>();

        var stockRepo = new Mock<IStockRepository>();
        var stockFactory = new Mock<IStockRepositoryFactory>();
        stockFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(stockRepo.Object);

        var tenantConn = new Mock<ITenantConnectionFactory>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);
        currentUser.SetupGet(u => u.WarehouseId).Returns(hasWarehouse ? WarehouseId : (Guid?)null);

        var ctrl = new PutawayController(
            service.Object, stockFactory.Object, tenantConn.Object,
            tenant.Object, currentUser.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, service, stockRepo, tenantConn, currentUserId);
    }

    private static Stock NewStock(Guid? id = null, decimal onHand = 50m) => new()
    {
        Id = id ?? Guid.NewGuid(),
        LocationId = Guid.NewGuid(),
        ProductId = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        UomId = Guid.NewGuid(),
        QuantityOnHand = onHand,
        QuantityAllocated = 0m,
        CreatedAt = DateTime.UtcNow.AddHours(-2),
    };

    private static PutawayQueueRow RowFor(Stock stock) =>
        new(StockId: stock.Id,
            QuantityOnHand: stock.QuantityOnHand,
            LocationId: stock.LocationId,
            LocationCode: "RECV-01",
            ZoneType: "Receiving",
            ProductId: stock.ProductId,
            ProductCode: "PROD-A001",
            ProductName: "Premium Widget",
            TrackingMethod: "None",
            OwnerId: stock.OwnerId,
            OwnerCode: "ACME",
            LotId: stock.LotId,
            LotNumber: null,
            PalletId: stock.PalletId,
            PalletNumber: null,
            UomId: stock.UomId,
            UomCode: "EA",
            LastMovementAt: null,
            CreatedAt: stock.CreatedAt);

    private static SuggestedLocationResult NewSuggestion(Guid? locId = null) =>
        new(LocationId: locId ?? Guid.NewGuid(),
            LocationCode: "A-03-15-B",
            ZoneCode: "STORE-A",
            ZoneName: "Storage Zone A",
            BinRank: 25,
            IsPickface: false,
            SameProductRowCount: 2,
            Reasons: new[] { "Same product nearby (2 stock rows)", "Low bin rank (25)" });

    // ================================================================
    // GET /putaway — queue
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
    public async Task Index_Happy_ReturnsViewWithQueueRows()
    {
        var b = BuildController();
        var stock = NewStock();
        b.StockRepo.Setup(r => r.GetPutawayQueueAsync(WarehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PutawayQueueRow> { RowFor(stock), RowFor(NewStock()) });

        var result = await b.Controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<PutawayQueueRow>>(view.Model);
        Assert.Equal(2, rows.Count);
    }

    // ================================================================
    // GET /putaway/{stockId} — task page
    // ================================================================

    [Fact]
    public async Task Task_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Task(Guid.NewGuid(), CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
    }

    [Fact]
    public async Task Task_StockNotFound_Returns404()
    {
        var b = BuildController();
        b.StockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stock?)null);

        var result = await b.Controller.Task(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Task_StockEmpty_Returns404()
    {
        var b = BuildController();
        var stock = NewStock(onHand: 0m);
        b.StockRepo.Setup(r => r.GetByIdAsync(stock.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);

        var result = await b.Controller.Task(stock.Id, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Task_StockNotInQueue_Returns404()
    {
        // Stock exists with positive OnHand BUT isn't at a staging zone
        // (queue read returns empty / different rows). Mobile shouldn't
        // let operator put away non-staging stock — that's a Transfer
        // workflow not Putaway.
        var b = BuildController();
        var stock = NewStock();
        b.StockRepo.Setup(r => r.GetByIdAsync(stock.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        b.StockRepo.Setup(r => r.GetPutawayQueueAsync(WarehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PutawayQueueRow>());   // empty queue

        var result = await b.Controller.Task(stock.Id, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Task_Happy_LoadsViewWithSuggestion()
    {
        var b = BuildController();
        var stock = NewStock();
        var suggestion = NewSuggestion();
        b.StockRepo.Setup(r => r.GetByIdAsync(stock.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        b.StockRepo.Setup(r => r.GetPutawayQueueAsync(WarehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PutawayQueueRow> { RowFor(stock) });
        b.StockRepo.Setup(r => r.GetSuggestedPutawayLocationAsync(
                WarehouseId, stock.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestion);

        var result = await b.Controller.Task(stock.Id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(suggestion, view.ViewData["Suggestion"]);
        var row = Assert.IsType<PutawayQueueRow>(view.Model);
        Assert.Equal(stock.Id, row.StockId);
    }

    [Fact]
    public async Task Task_Happy_NoSuggestion_StillRenders()
    {
        // Algorithm returns null when no Storage-zone location exists —
        // view renders with the amber "no suggestion" callout instead
        // of blowing up.
        var b = BuildController();
        var stock = NewStock();
        b.StockRepo.Setup(r => r.GetByIdAsync(stock.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        b.StockRepo.Setup(r => r.GetPutawayQueueAsync(WarehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PutawayQueueRow> { RowFor(stock) });
        b.StockRepo.Setup(r => r.GetSuggestedPutawayLocationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SuggestedLocationResult?)null);

        var result = await b.Controller.Task(stock.Id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Null(view.ViewData["Suggestion"]);
    }

    // ================================================================
    // POST /putaway/submit/{stockId}
    // ================================================================

    [Fact]
    public async Task Submit_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Submit(
            Guid.NewGuid(), new MobilePutawaySubmitViewModel(), CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
    }

    [Fact]
    public async Task Submit_ZeroQuantity_RedirectsBackWithError_NoServiceCall()
    {
        var b = BuildController();
        var result = await b.Controller.Submit(
            Guid.NewGuid(),
            new MobilePutawaySubmitViewModel { Quantity = 0m },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("positive", b.Controller.TempData["PutawayError"] as string);
        b.Service.Verify(s => s.PutawayStockAsync(
            It.IsAny<Guid>(), It.IsAny<PutawayRequest>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_StockNotFound_Returns404()
    {
        var b = BuildController();
        b.StockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stock?)null);

        var result = await b.Controller.Submit(
            Guid.NewGuid(),
            new MobilePutawaySubmitViewModel { Quantity = 5m },
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Submit_NoOverride_NoSuggestion_RedirectsBackWithError()
    {
        var b = BuildController();
        var stock = NewStock();
        b.StockRepo.Setup(r => r.GetByIdAsync(stock.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        b.StockRepo.Setup(r => r.GetSuggestedPutawayLocationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SuggestedLocationResult?)null);

        var result = await b.Controller.Submit(
            stock.Id,
            new MobilePutawaySubmitViewModel { Quantity = 5m, ToLocationCode = null },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("scan a target bin", b.Controller.TempData["PutawayError"] as string);
        b.Service.Verify(s => s.PutawayStockAsync(
            It.IsAny<Guid>(), It.IsAny<PutawayRequest>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_HappyWithSuggestion_BouncesToQueue()
    {
        var b = BuildController();
        var stock = NewStock();
        var suggestion = NewSuggestion();
        var destStock = NewStock(onHand: 100m);

        b.StockRepo.Setup(r => r.GetByIdAsync(stock.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        b.StockRepo.Setup(r => r.GetSuggestedPutawayLocationAsync(
                WarehouseId, stock.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestion);
        b.Service.Setup(s => s.PutawayStockAsync(
                TenantId,
                It.Is<PutawayRequest>(req =>
                    req.FromKey.LocationId == stock.LocationId
                    && req.FromKey.ProductId == stock.ProductId
                    && req.ToLocationId == suggestion.LocationId
                    && req.Quantity == 5m),
                b.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutawayResult(stock, destStock));

        var result = await b.Controller.Submit(
            stock.Id,
            new MobilePutawaySubmitViewModel { Quantity = 5m, ToLocationCode = null },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        var msg = b.Controller.TempData["PutawayMessage"] as string;
        Assert.Contains("Moved 5", msg);
        Assert.Contains("100", msg);   // destination OnHand
    }

    [Fact]
    public async Task Submit_ServiceThrows_RedirectsBackWithError()
    {
        var b = BuildController();
        var stock = NewStock();
        b.StockRepo.Setup(r => r.GetByIdAsync(stock.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        b.StockRepo.Setup(r => r.GetSuggestedPutawayLocationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewSuggestion());
        b.Service.Setup(s => s.PutawayStockAsync(
                It.IsAny<Guid>(), It.IsAny<PutawayRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Insufficient quantity at source."));

        var result = await b.Controller.Submit(
            stock.Id,
            new MobilePutawaySubmitViewModel { Quantity = 5m },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("Insufficient", b.Controller.TempData["PutawayError"] as string);
    }
}
