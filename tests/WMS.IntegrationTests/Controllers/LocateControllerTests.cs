using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inventory;
using WMS.Web.Controllers;

namespace WMS.IntegrationTests.Controllers;

// Phase 22 — Mobile Locate PWA controller tests. Mirrors Phase 18-21
// shape (constructor injection + factory mocks).
//
// /locate/search and /locate/loc/{id}/header-lookup use inline
// ITenantConnectionFactory + raw Dapper which can't be cleanly mocked
// (TD-041 family — same as Phase 18 ReceiveController, Phase 20
// PutawayController). What WE can test: the no-warehouse guards, the
// view dispatch shape on Index, and the service-call sites that use
// IStockRepositoryFactory (NOT inline tenantConn).
//
// What's NOT covered here:
// - Search routing happy path (inline product/location resolver)
// - Item view product-not-found 404 (inline product header lookup)
// - Loc view location-not-found / cross-warehouse 404 (inline lookup)
// → all TD-041 family. Logged once collectively.
public class LocateControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    private record Build(
        LocateController Controller,
        Mock<IStockRepository> StockRepo,
        Mock<ITenantConnectionFactory> TenantConn,
        Guid CurrentUserId);

    private static Build BuildController(bool hasWarehouse = true)
    {
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

        var ctrl = new LocateController(
            stockFactory.Object, tenantConn.Object, tenant.Object, currentUser.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, stockRepo, tenantConn, currentUserId);
    }

    // ================================================================
    // GET /locate — search entry
    // ================================================================

    [Fact]
    public void Index_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = b.Controller.Index();
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
        Assert.Equal("Auth", redirect.ControllerName);
    }

    [Fact]
    public void Index_Happy_ReturnsView()
    {
        var b = BuildController();
        var result = b.Controller.Index();
        Assert.IsType<ViewResult>(result);
    }

    // ================================================================
    // GET /locate/search?q=...
    // ================================================================

    [Fact]
    public async Task Search_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Search("PROD-A001", CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
    }

    [Fact]
    public async Task Search_BlankQuery_RedirectsToIndex_NoLookup()
    {
        var b = BuildController();
        var result = await b.Controller.Search("  ", CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        // ITenantConnectionFactory.CreateConnection should NEVER be
        // called when query is blank — early bail-out.
        b.TenantConn.Verify(t => t.CreateConnection(It.IsAny<Guid>()), Times.Never);
    }

    // ================================================================
    // GET /locate/item/{productId}
    // ================================================================

    [Fact]
    public async Task Item_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Item(Guid.NewGuid(), CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
    }

    // ================================================================
    // GET /locate/loc/{locationId}
    // ================================================================

    [Fact]
    public async Task Loc_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Loc(Guid.NewGuid(), CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
    }
}
