using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.BLL.Services.Inbound;
using WMS.Common.Auth;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Inbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Inbound;
using WMS.Web.Services.Storage;

namespace WMS.IntegrationTests.Controllers;

// Phase 9A admin-side tests for PurchaseOrdersController. Mirrors the
// Phase 7F BuildAdmin pattern — separate richer helper exposing all
// mocks; existing read-side test ergonomics preserved when those land.
public class PurchaseOrdersAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        PurchaseOrdersController Controller,
        Mock<IPurchaseOrderRepository> Repo,
        Mock<IReceivingHeaderRepository> ReceivingRepo,
        Mock<IPurchaseOrderService> Service,
        Mock<IOwnerRepository> OwnerRepo,
        Mock<IProductRepository> ProductRepo,
        Mock<IUomRepository> UomRepo,
        Mock<IWarehouseRepository> WarehouseRepo,
        Mock<IValidator<PurchaseOrderCreateViewModel>> CreateValidator,
        Mock<IValidator<PurchaseOrderEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo            = new Mock<IPurchaseOrderRepository>();
        var factory         = new Mock<IPurchaseOrderRepositoryFactory>();
        var receivingRepo   = new Mock<IReceivingHeaderRepository>();
        var receivingFactory = new Mock<IReceivingHeaderRepositoryFactory>();
        var tenant          = new Mock<ITenantContext>();

        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);
        receivingFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(receivingRepo.Object);
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        // TD-028 / TD-029 / TD-030 — default-stub the new read methods so
        // unrelated admin tests (Create/Edit/Archive) keep compiling.
        // Detail-specific tests override individually below.
        repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<PurchaseOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderStatusCounts(0, 0, 0, 0, 0));
        repo.Setup(r => r.GetLineRowsByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PurchaseOrderLineRow>());
        receivingRepo.Setup(r => r.GetReceiptsByPoIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PoReceiptRow>());
        receivingRepo.Setup(r => r.GetActivityByPoAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ReceivingActivityRow>());

        var docs = Mock.Of<IDocumentStorageService>(d =>
            d.ListByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<DocumentMetadata>()));

        var ownerRepo    = new Mock<IOwnerRepository>();
        ownerRepo.Setup(r => r.GetActiveSuppliersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(Guid.NewGuid(), "ACME-SUP", "Acme Supplies"),
            });
        var ownerFactory = new Mock<IOwnerRepositoryFactory>();
        ownerFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(ownerRepo.Object);

        var productRepo  = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(Guid.NewGuid(), "PROD-1", "Product 1"),
            });
        var productFactory = new Mock<IProductRepositoryFactory>();
        productFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(productRepo.Object);

        var uomRepo      = new Mock<IUomRepository>();
        uomRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(Guid.NewGuid(), "EA", "Each"),
            });
        var uomFactory   = new Mock<IUomRepositoryFactory>();
        uomFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(uomRepo.Object);

        var warehouseRepo    = new Mock<IWarehouseRepository>();
        warehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo>
            {
                new(Guid.NewGuid(), "WH-MAIN", "Main Warehouse"),
            });
        var warehouseFactory = new Mock<IWarehouseRepositoryFactory>();
        warehouseFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(warehouseRepo.Object);

        var service = new Mock<IPurchaseOrderService>();

        var createValidator = new Mock<IValidator<PurchaseOrderCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<PurchaseOrderCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var editValidator = new Mock<IValidator<PurchaseOrderEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<PurchaseOrderEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);
        currentUser.SetupGet(u => u.WarehouseId).Returns((Guid?)null);

        var ctrl = new PurchaseOrdersController(
            factory.Object,
            receivingFactory.Object,
            tenant.Object,
            docs,
            ownerFactory.Object,
            warehouseFactory.Object,
            productFactory.Object,
            uomFactory.Object,
            service.Object,
            createValidator.Object,
            editValidator.Object,
            currentUser.Object);

        return new AdminBuild(
            ctrl, repo, receivingRepo, service,
            ownerRepo, productRepo, uomRepo, warehouseRepo,
            createValidator, editValidator, userId);
    }

    private static PurchaseOrderDetail SampleDetail(string poNumber = "PO-001", int receivedLines = 0)
    {
        var poId = Guid.NewGuid();
        var lines = new List<PurchaseOrderLine>
        {
            new()
            {
                Id = Guid.NewGuid(), PurchaseOrderId = poId, LineNumber = 1,
                ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                ExpectedQuantity = 10m,
                ReceivedQuantity = receivedLines > 0 ? 3m : 0m,
                Status = receivedLines > 0 ? "PartiallyReceived" : "Open",
            },
        };
        var header = new PurchaseOrder
        {
            Id = poId,
            PoNumber = poNumber,
            OwnerId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            ExpectedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Status = "Open",
            Notes = "Test",
        };
        return new PurchaseOrderDetail(header, lines);
    }

    [Fact]
    public async Task Create_Get_PopulatesLookups()
    {
        var b = BuildAdmin();
        var result = await b.Controller.Create(default);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PurchaseOrderCreateViewModel>(view.Model);
        Assert.Single(vm.Vendors);
        Assert.Single(vm.Warehouses);
        Assert.Single(vm.Products);
        Assert.Single(vm.Uoms);
        // One blank starter row for the grid.
        Assert.Single(vm.Lines);
    }

    [Fact]
    public async Task Create_Post_FluentValidationFails_AddsErrors()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<PurchaseOrderCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("PoNumber", "PO number 'X' is already in use."),
            }));

        var vm = new PurchaseOrderCreateViewModel { PoNumber = "X" };
        var result = await b.Controller.Create(vm, default);

        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
        Assert.Contains(b.Controller.ModelState["PoNumber"]!.Errors,
            e => e.ErrorMessage.Contains("already in use"));
    }

    [Fact]
    public async Task Create_Post_HappyPath_CallsService_AndRedirects()
    {
        var b = BuildAdmin();
        var newPoId = Guid.NewGuid();
        var detail = SampleDetail("PO-NEW") with
        {
            Header = new PurchaseOrder
            {
                Id = newPoId,
                PoNumber = "PO-NEW",
                OwnerId = Guid.NewGuid(),
                WarehouseId = Guid.NewGuid(),
                Status = "Open",
            },
        };
        CreatePurchaseOrderRequest? captured = null;
        b.Service.Setup(s => s.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<CreatePurchaseOrderRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CreatePurchaseOrderRequest, Guid?, CancellationToken>((_, r, _, _) => captured = r)
            .ReturnsAsync(detail);

        var vm = new PurchaseOrderCreateViewModel
        {
            PoNumber = "  PO-NEW  ",
            OwnerId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            ExpectedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "  some notes  ",
            Lines = new List<PurchaseOrderLineViewModel>
            {
                new() { LineNumber = 1, ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(), ExpectedQuantity = 5m },
            },
        };

        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal(newPoId, redirect.RouteValues!["id"]);
        Assert.NotNull(captured);
        Assert.Equal("PO-NEW", captured!.PoNumber);
        Assert.Equal("some notes", captured.Notes);
        Assert.Single(captured.Lines);
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderDetail?)null);

        var result = await b.Controller.Edit(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_Vm_WithLinesUnlocked_WhenZeroReceipts()
    {
        var b = BuildAdmin();
        var detail = SampleDetail("PO-A", receivedLines: 0);
        b.Repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.Repo.Setup(r => r.CountReceivedLinesAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await b.Controller.Edit(detail.Header.Id, default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PurchaseOrderEditViewModel>(view.Model);
        Assert.False(vm.LinesLocked);
        Assert.Equal("PO-A", vm.PoNumber);
        Assert.Single(vm.Lines);
    }

    [Fact]
    public async Task Edit_Get_Loads_Vm_WithLinesLocked_WhenReceiptsExist()
    {
        var b = BuildAdmin();
        var detail = SampleDetail("PO-B", receivedLines: 1);
        b.Repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.Repo.Setup(r => r.CountReceivedLinesAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await b.Controller.Edit(detail.Header.Id, default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PurchaseOrderEditViewModel>(view.Model);
        Assert.True(vm.LinesLocked);
    }

    [Fact]
    public async Task Edit_Post_HappyPath_CallsUpdate_AndRedirects()
    {
        var b = BuildAdmin();
        var poId = Guid.NewGuid();
        b.Repo.Setup(r => r.CountReceivedLinesAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        UpdatePurchaseOrderRequest? captured = null;
        b.Service.Setup(s => s.UpdateAsync(
                It.IsAny<Guid>(), poId, It.IsAny<UpdatePurchaseOrderRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, UpdatePurchaseOrderRequest, Guid?, CancellationToken>((_, _, r, _, _) => captured = r)
            .ReturnsAsync(SampleDetail("PO-X"));

        var vm = new PurchaseOrderEditViewModel
        {
            Id = poId,
            PoNumber = "PO-X",
            ExpectedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "edited",
            Lines = new List<PurchaseOrderLineViewModel>
            {
                new() { LineNumber = 1, ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(), ExpectedQuantity = 7m },
            },
        };

        var result = await b.Controller.Edit(poId, vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal(poId, redirect.RouteValues!["id"]);
        Assert.NotNull(captured);
        Assert.True(captured!.ReplaceLines, "Lines should be replaced when not locked");
        Assert.Equal("edited", captured.Notes);
    }

    [Fact]
    public async Task Edit_Post_LockedLines_ReplaceLinesFalse()
    {
        var b = BuildAdmin();
        var poId = Guid.NewGuid();
        b.Repo.Setup(r => r.CountReceivedLinesAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        UpdatePurchaseOrderRequest? captured = null;
        b.Service.Setup(s => s.UpdateAsync(
                It.IsAny<Guid>(), poId, It.IsAny<UpdatePurchaseOrderRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, UpdatePurchaseOrderRequest, Guid?, CancellationToken>((_, _, r, _, _) => captured = r)
            .ReturnsAsync(SampleDetail("PO-X"));

        var vm = new PurchaseOrderEditViewModel
        {
            Id = poId,
            PoNumber = "PO-X",
            ExpectedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "header-only edit",
            Lines = new List<PurchaseOrderLineViewModel>(),
            LinesLocked = false,  // lying — server overrides
        };

        await b.Controller.Edit(poId, vm, default);

        Assert.NotNull(captured);
        // Server-side lock check overrode VM's LinesLocked=false.
        Assert.False(captured!.ReplaceLines);
    }

    [Fact]
    public async Task Archive_Post_HappyPath_CallsArchive_AndRedirects()
    {
        var b = BuildAdmin();
        var poId = Guid.NewGuid();
        b.Service.Setup(s => s.ArchiveAsync(
                It.IsAny<Guid>(), poId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.Archive(poId, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal(poId, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task Archive_Post_NoOp_BadRequest()
    {
        var b = BuildAdmin();
        b.Service.Setup(s => s.ArchiveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await b.Controller.Archive(Guid.NewGuid(), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("Open",      "info")]
    [InlineData("Receiving", "warning")]
    [InlineData("Closed",    "success")]
    [InlineData("Cancelled", "neutral")]
    public void StatusMapper_RoundTrips(string db, string _)
    {
        var wire = WMS.Web.Services.Mappers.PurchaseOrderStatusMapper.ToWire(db);
        var back = WMS.Web.Services.Mappers.PurchaseOrderStatusMapper.FromWire(wire);
        Assert.Equal(db, back);
    }

    // ================================================================
    // Phase 10A — Detail tabs + chip counts
    // ================================================================

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderDetail?)null);

        var result = await b.Controller.Detail(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    // TD-029 — Lines tab is wired to the resolved-row repo method and
    // surfaces as a CustomTab on the DetailPageViewModel.
    [Fact]
    public async Task Detail_PopulatesLinesTab_FromResolvedRows()
    {
        var b = BuildAdmin();
        var detail = SampleDetail("PO-LINES");
        var lineRows = new List<PurchaseOrderLineRow>
        {
            new(Guid.NewGuid(), 1, Guid.NewGuid(), "PROD-1", "Product One",
                Guid.NewGuid(), "EA", 10m, 4m, "PartiallyReceived"),
            new(Guid.NewGuid(), 2, Guid.NewGuid(), "PROD-2", "Product Two",
                Guid.NewGuid(), "BOX", 5m, 5m, "Closed"),
        };
        b.Repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.Repo.Setup(r => r.GetLineRowsByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineRows);

        var result = await b.Controller.Detail(detail.Header.Id, default);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Shared/_DetailLayout.cshtml", view.ViewName);

        var vm = Assert.IsType<WMS.Web.ViewModels.Detail.DetailPageViewModel>(view.Model);
        var lines = vm.CustomTabs.FirstOrDefault(t => t.Key == "lines");
        Assert.NotNull(lines);
        Assert.Equal(2, lines!.Count);
        Assert.Equal("Detail/_PoLinesPanel", lines.PartialName);

        // ViewBag.PoLines carries the actual rows for the partial.
        Assert.Same(lineRows, view.ViewData["PoLines"]);
    }

    // TD-030 — Receipts tab is wired to GetReceiptsByPoIdAsync and the
    // count badge equals the receipt count.
    [Fact]
    public async Task Detail_PopulatesReceiptsTab_FromReceiptRows()
    {
        var b = BuildAdmin();
        var detail = SampleDetail("PO-RCPT");
        var receipts = new List<PoReceiptRow>
        {
            new(Guid.NewGuid(), "GR-001", DateTime.UtcNow.AddDays(-1), "Posted", 3, 12m),
            new(Guid.NewGuid(), "GR-002", DateTime.UtcNow,             "Draft",  1,  4m),
        };
        b.Repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        b.ReceivingRepo.Setup(r => r.GetReceiptsByPoIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipts);

        var result = await b.Controller.Detail(detail.Header.Id, default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<WMS.Web.ViewModels.Detail.DetailPageViewModel>(view.Model);

        var receiptsTab = vm.CustomTabs.FirstOrDefault(t => t.Key == "receipts");
        Assert.NotNull(receiptsTab);
        Assert.Equal(2, receiptsTab!.Count);
        Assert.Same(receipts, view.ViewData["PoReceipts"]);
    }

    // TD-029 + TD-030 — declaration order is Lines, then Receipts.
    [Fact]
    public async Task Detail_CustomTabs_AreInExpectedOrder()
    {
        var b = BuildAdmin();
        var detail = SampleDetail("PO-ORDER");
        b.Repo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await b.Controller.Detail(detail.Header.Id, default);
        var vm = Assert.IsType<WMS.Web.ViewModels.Detail.DetailPageViewModel>(
            ((ViewResult)result).Model);
        Assert.Equal(new[] { "lines", "receipts" },
            vm.CustomTabs.Select(t => t.Key).ToArray());
    }

    // TD-028 — chip counts on the JSON envelope.
    [Fact]
    public async Task GetData_ReturnsCountsAlongsideRows()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<PurchaseOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderStatusCounts(
                All: 24, Open: 10, Receiving: 8, Closed: 5, Cancelled: 1));
        b.Repo.Setup(r => r.GetPagedAsync(
                It.IsAny<PurchaseOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PurchaseOrderListRow>
            {
                Items = new(), Total = 24, Page = 1, PageSize = 20, TotalPages = 2,
            });

        var json = Assert.IsType<JsonResult>(await b.Controller.GetData());
        var envelope = json.Value!;
        var counts = envelope.GetType().GetProperty("counts")!.GetValue(envelope)!;
        Assert.Equal(24, counts.GetType().GetProperty("all")!.GetValue(counts));
        Assert.Equal(10, counts.GetType().GetProperty("open")!.GetValue(counts));
        Assert.Equal(8,  counts.GetType().GetProperty("receiving")!.GetValue(counts));
        Assert.Equal(5,  counts.GetType().GetProperty("closed")!.GetValue(counts));
        Assert.Equal(1,  counts.GetType().GetProperty("cancelled")!.GetValue(counts));
    }
}
