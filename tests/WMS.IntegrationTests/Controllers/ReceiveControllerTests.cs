using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Inbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Inbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Inbound;

namespace WMS.IntegrationTests.Controllers;

// Phase 18 — ReceiveController tests. Mobile Receive PWA replaces
// the Phase 1 single-page ReceiveController; same /receive routes,
// new queue + per-line task UX. Mirrors Phase 16 PickControllerTests
// shape (constructor injection + factory mocks).
//
// Submit happy path is NOT exercised end-to-end here — the location-
// resolver inline call uses HttpContext.RequestServices.GetRequired-
// Service which can't be easily mocked without a service provider
// fixture (TD-041, same family as TD-006 SQL fixture). Submit error
// paths (404 PO, blank reason on cancel, etc.) are covered.
public class ReceiveControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    private record Build(
        ReceiveController Controller,
        Mock<IReceivingHeaderService> Service,
        Mock<IPurchaseOrderRepository> PoRepo,
        Mock<IProductRepository> ProductRepo,
        Guid CurrentUserId);

    private static Build BuildController(bool hasWarehouse = true)
    {
        var service = new Mock<IReceivingHeaderService>();

        var poRepo = new Mock<IPurchaseOrderRepository>();
        var poFactory = new Mock<IPurchaseOrderRepositoryFactory>();
        poFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(poRepo.Object);

        var productRepo = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetMetaByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ProductLineMeta>());
        var productFactory = new Mock<IProductRepositoryFactory>();
        productFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(productRepo.Object);

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);
        currentUser.SetupGet(u => u.WarehouseId).Returns(hasWarehouse ? WarehouseId : (Guid?)null);

        var ctrl = new ReceiveController(
            service.Object, poFactory.Object, productFactory.Object,
            tenant.Object, currentUser.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, service, poRepo, productRepo, currentUserId);
    }

    private static PurchaseOrderListRow NewListRow(string status, string number) =>
        new(Id: Guid.NewGuid(),
            PoNumber: number,
            OwnerId: Guid.NewGuid(),
            OwnerCode: "ACME",
            OwnerName: "Acme Supplier",
            WarehouseId: WarehouseId,
            WarehouseCode: "WH-MAIN",
            ExpectedDate: DateTime.UtcNow.AddDays(1),
            Status: status,
            LineCount: 3,
            TotalExpectedQty: 50m,
            TotalReceivedQty: 0m,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null);

    // ================================================================
    // GET /receive — queue
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
    public async Task Index_Happy_MergesReceivingThenOpen()
    {
        var b = BuildController();
        var openRow = NewListRow("Open", "PO-OPEN-1");
        var receivingRow = NewListRow("Receiving", "PO-RECV-1");

        b.PoRepo.Setup(r => r.GetPagedAsync(
                It.Is<PurchaseOrderFilter>(f => f.Status == "Open"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PurchaseOrderListRow>
            {
                Items = new List<PurchaseOrderListRow> { openRow },
                Total = 1, Page = 1, PageSize = 50, TotalPages = 1,
            });
        b.PoRepo.Setup(r => r.GetPagedAsync(
                It.Is<PurchaseOrderFilter>(f => f.Status == "Receiving"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PurchaseOrderListRow>
            {
                Items = new List<PurchaseOrderListRow> { receivingRow },
                Total = 1, Page = 1, PageSize = 50, TotalPages = 1,
            });

        var result = await b.Controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<PurchaseOrderListRow>>(view.Model!);
        Assert.Equal(2, rows.Count);
        // Receiving first (returning operator), Open below.
        Assert.Equal("PO-RECV-1", rows[0].PoNumber);
        Assert.Equal("PO-OPEN-1", rows[1].PoNumber);
    }

    // ================================================================
    // GET /receive/{poId} — task page
    // ================================================================

    [Fact]
    public async Task Task_NotFound_Returns404()
    {
        var b = BuildController();
        b.PoRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderDetail?)null);

        var result = await b.Controller.Task(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("Cancelled")]
    public async Task Task_TerminalStatus_Returns404(string status)
    {
        var b = BuildController();
        var poId = Guid.NewGuid();
        b.PoRepo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderDetail(
                new PurchaseOrder { Id = poId, PoNumber = "PO-X", Status = status, OwnerId = Guid.NewGuid(), WarehouseId = WarehouseId },
                new List<PurchaseOrderLine>()));

        var result = await b.Controller.Task(poId, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Task_Happy_LoadsProductMetadata()
    {
        var b = BuildController();
        var poId = Guid.NewGuid();
        var productAId = Guid.NewGuid();
        b.PoRepo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderDetail(
                new PurchaseOrder { Id = poId, PoNumber = "PO-X", Status = "Open", OwnerId = Guid.NewGuid(), WarehouseId = WarehouseId },
                new List<PurchaseOrderLine>
                {
                    new() { Id = Guid.NewGuid(), PurchaseOrderId = poId, LineNumber = 1, ProductId = productAId, UomId = Guid.NewGuid(), ExpectedQuantity = 10m, Status = "Open" },
                }));
        b.ProductRepo.Setup(r => r.GetMetaByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ProductLineMeta>
            {
                [productAId] = new("PROD-A", "Product A", "Lot"),
            });

        var result = await b.Controller.Task(poId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var meta = view.ViewData["ProductMeta"] as IReadOnlyDictionary<Guid, ProductLineMeta>;
        Assert.NotNull(meta);
        Assert.Equal("PROD-A", meta![productAId].Code);
    }

    // ================================================================
    // POST /receive/cancel/{poId}
    // ================================================================

    [Fact]
    public void Cancel_BlankReason_RedirectsWithDiscardedMessage()
    {
        var b = BuildController();
        var result = b.Controller.Cancel(Guid.NewGuid(), reason: "  ");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("discarded", b.Controller.TempData["ReceiveMessage"]?.ToString() ?? "");
    }

    [Fact]
    public void Cancel_WithReason_IncludesReasonInMessage()
    {
        var b = BuildController();
        b.Controller.Cancel(Guid.NewGuid(), reason: "operator break");

        Assert.Contains("operator break", b.Controller.TempData["ReceiveMessage"]?.ToString() ?? "");
    }

    // ================================================================
    // POST /receive/submit/{poId} — error paths only
    // (Happy path uses an inline location resolver via HttpContext
    // service-locator that needs a real service provider fixture —
    // TD-041 in the same family as TD-006.)
    // ================================================================

    [Fact]
    public async Task Submit_NoWarehouse_RedirectsToSelectWarehouse()
    {
        var b = BuildController(hasWarehouse: false);
        var result = await b.Controller.Submit(
            Guid.NewGuid(), new MobileReceiveSubmitViewModel(), CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SelectWarehouse", redirect.ActionName);
        Assert.Equal("Auth", redirect.ControllerName);
    }

    [Fact]
    public async Task Submit_PoNotFound_Returns404()
    {
        var b = BuildController();
        b.PoRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderDetail?)null);

        var result = await b.Controller.Submit(
            Guid.NewGuid(), new MobileReceiveSubmitViewModel(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Submit_AllLinesBlank_RedirectsBackWithError()
    {
        var b = BuildController();
        var poId = Guid.NewGuid();
        b.PoRepo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderDetail(
                new PurchaseOrder { Id = poId, PoNumber = "PO-X", Status = "Open", OwnerId = Guid.NewGuid(), WarehouseId = WarehouseId },
                new List<PurchaseOrderLine>()));

        // Empty submission — operator submitted with no quantities.
        var result = await b.Controller.Submit(
            poId, new MobileReceiveSubmitViewModel { Lines = new() }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        Assert.Contains("at least one line", b.Controller.TempData["ReceiveError"]?.ToString() ?? "");
        // Service NOT called.
        b.Service.Verify(s => s.PostReceivingAsync(
            It.IsAny<Guid>(), It.IsAny<PostReceivingRequest>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_SerialTrackedLine_RejectedWithUseDesktopMessage()
    {
        var b = BuildController();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        b.PoRepo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderDetail(
                new PurchaseOrder { Id = poId, PoNumber = "PO-X", Status = "Open", OwnerId = Guid.NewGuid(), WarehouseId = WarehouseId },
                new List<PurchaseOrderLine>
                {
                    new() { Id = poLineId, PurchaseOrderId = poId, LineNumber = 1, ProductId = productId, UomId = Guid.NewGuid(), ExpectedQuantity = 5m, Status = "Open" },
                }));
        b.ProductRepo.Setup(r => r.GetMetaByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ProductLineMeta>
            {
                [productId] = new("PROD-S", "Serial Product", "LotAndSerial"),
            });

        var vm = new MobileReceiveSubmitViewModel
        {
            Lines = new()
            {
                new() { PoLineId = poLineId, ReceivedQuantity = 5m, LocationCode = "WH-A01" },
            },
        };

        var result = await b.Controller.Submit(poId, vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Task", redirect.ActionName);
        var err = b.Controller.TempData["ReceiveError"]?.ToString() ?? "";
        Assert.Contains("serial-tracked", err);
        Assert.Contains("desktop", err);
        // Service NOT called — guard short-circuits.
        b.Service.Verify(s => s.PostReceivingAsync(
            It.IsAny<Guid>(), It.IsAny<PostReceivingRequest>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
