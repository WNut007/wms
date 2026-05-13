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
    public async Task Edit_Post_HappyPath_UnlockedLine_ClassifiedAsUpdate()
    {
        // d.2.3.a — controller switched to UpdatePartialAsync. Posted
        // line matches DB by LineNumber + DB has zero receipts → update.
        var b = BuildAdmin();
        var detail = SampleDetail("PO-X", receivedLines: 0);
        var poId = detail.Header.Id;
        var dbLineId = detail.Lines[0].Id;
        b.Repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        PartialUpdatePurchaseOrderRequest? captured = null;
        b.Service.Setup(s => s.UpdatePartialAsync(
                It.IsAny<Guid>(), poId, It.IsAny<PartialUpdatePurchaseOrderRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, PartialUpdatePurchaseOrderRequest, Guid?, CancellationToken>(
                (_, _, r, _, _) => captured = r)
            .ReturnsAsync(detail);

        var postedProductId = Guid.NewGuid();
        var postedUomId = Guid.NewGuid();
        var vm = new PurchaseOrderEditViewModel
        {
            Id = poId,
            PoNumber = "PO-X",
            ExpectedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "edited",
            Lines = new List<PurchaseOrderLineViewModel>
            {
                new() { LineNumber = 1, ProductId = postedProductId, UomId = postedUomId, ExpectedQuantity = 7m },
            },
        };

        var result = await b.Controller.Edit(poId, vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal(poId, redirect.RouteValues!["id"]);
        Assert.NotNull(captured);
        Assert.Single(captured!.LineUpdates);
        Assert.Empty(captured.LineInserts);
        Assert.Empty(captured.LineDeletes);
        // Classification by LineNumber resolves the DB line's Id.
        Assert.Equal(dbLineId, captured.LineUpdates[0].LineId);
        Assert.Equal(postedProductId, captured.LineUpdates[0].ProductId);
        Assert.Equal(7m, captured.LineUpdates[0].ExpectedQuantity);
        Assert.Equal("edited", captured.Notes);
    }

    [Fact]
    public async Task Edit_Post_LockedLines_FilteredOut_EmptyLineOps()
    {
        // d.2.3.a — when DB line has receipts, the round-tripped POST
        // body's value for that line is dropped (operator's intent
        // matches "don't change"; server-side filter enforces it).
        // Service is called with empty LineUpdates / Inserts / Deletes.
        var b = BuildAdmin();
        var detail = SampleDetail("PO-LOCKED", receivedLines: 2);  // line 1 has ReceivedQty>0
        var poId = detail.Header.Id;
        b.Repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        PartialUpdatePurchaseOrderRequest? captured = null;
        b.Service.Setup(s => s.UpdatePartialAsync(
                It.IsAny<Guid>(), poId, It.IsAny<PartialUpdatePurchaseOrderRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, PartialUpdatePurchaseOrderRequest, Guid?, CancellationToken>(
                (_, _, r, _, _) => captured = r)
            .ReturnsAsync(detail);

        var vm = new PurchaseOrderEditViewModel
        {
            Id = poId,
            PoNumber = "PO-LOCKED",
            ExpectedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "header-only edit",
            // d.2.2 UI round-trips locked lines in the POST body via
            // hidden inputs; the controller filter is what drops them.
            Lines = new List<PurchaseOrderLineViewModel>
            {
                new() { LineNumber = 1, ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(), ExpectedQuantity = 999m },
            },
            LinesLocked = false,  // lying — server overrides
        };

        await b.Controller.Edit(poId, vm, default);

        Assert.NotNull(captured);
        Assert.Empty(captured!.LineUpdates);
        Assert.Empty(captured.LineInserts);
        Assert.Empty(captured.LineDeletes);
        Assert.Equal("header-only edit", captured.Notes);
    }

    [Fact]
    public async Task Edit_Post_PartialLockPo_DisabledUoms_HeaderOnly_NoLineOps()
    {
        // d.2.3.a.1 regression — d.2.2 UI applies whole-grid lock when
        // any line has receipts, so ALL UoM <select>s are disabled
        // across the grid (locked + unlocked alike). Disabled selects
        // don't POST, so vm.Lines arrives with Guid.Empty UomId across
        // every row. Without the LinesLocked short-circuit, the
        // unlocked DB lines would be classified as updates with empty
        // UomId → service refuses with "UomId is required". Controller
        // must short-circuit to header-only when whole-grid lock is on.
        var b = BuildAdmin();
        var poId = Guid.NewGuid();

        // Two-line detail: line 1 received, line 2 not.
        var line1Id = Guid.NewGuid();
        var line2Id = Guid.NewGuid();
        var detail = new PurchaseOrderDetail(
            new PurchaseOrder
            {
                Id = poId, PoNumber = "PO-PART",
                OwnerId = Guid.NewGuid(), WarehouseId = Guid.NewGuid(),
                Status = "Receiving",
            },
            new List<PurchaseOrderLine>
            {
                new() {
                    Id = line1Id, PurchaseOrderId = poId, LineNumber = 1,
                    ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                    ExpectedQuantity = 10m, ReceivedQuantity = 5m,
                    Status = "PartiallyReceived",
                },
                new() {
                    Id = line2Id, PurchaseOrderId = poId, LineNumber = 2,
                    ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                    ExpectedQuantity = 8m, ReceivedQuantity = 0m,
                    Status = "Open",
                },
            });
        b.Repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        PartialUpdatePurchaseOrderRequest? captured = null;
        b.Service.Setup(s => s.UpdatePartialAsync(
                It.IsAny<Guid>(), poId, It.IsAny<PartialUpdatePurchaseOrderRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, PartialUpdatePurchaseOrderRequest, Guid?, CancellationToken>(
                (_, _, r, _, _) => captured = r)
            .ReturnsAsync(detail);

        // d.2.2 UI POST shape: both lines round-trip with empty UomId
        // (disabled selects don't POST). ProductId IS populated because
        // the Product cell uses a hidden input in locked mode (d.2.2).
        // ExpectedQuantity is populated because readonly inputs DO POST.
        var vm = new PurchaseOrderEditViewModel
        {
            Id = poId,
            PoNumber = "PO-PART",
            Notes = "header edit",
            Lines = new List<PurchaseOrderLineViewModel>
            {
                new() { LineNumber = 1, ProductId = Guid.NewGuid(),
                        UomId = Guid.Empty, ExpectedQuantity = 10m },
                new() { LineNumber = 2, ProductId = Guid.NewGuid(),
                        UomId = Guid.Empty, ExpectedQuantity = 8m },
            },
            LinesLocked = false,  // lying — server overrides to true
        };

        var line1OriginalUom = detail.Lines[0].UomId;
        var line2OriginalUom = detail.Lines[1].UomId;

        var result = await b.Controller.Edit(poId, vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);

        // Controller short-circuited to header-only because the server
        // re-derived LinesLocked = true.
        Assert.NotNull(captured);
        Assert.Empty(captured!.LineUpdates);
        Assert.Empty(captured.LineInserts);
        Assert.Empty(captured.LineDeletes);
        Assert.Equal("header edit", captured.Notes);

        // d.2.3.a.2 — repopulate brings vm.Lines into agreement with DB
        // so the error-path re-render shows real values (not the '—'
        // empty-option fallback the disabled-select POST would produce).
        Assert.Equal(line1OriginalUom, vm.Lines[0].UomId);
        Assert.Equal(line2OriginalUom, vm.Lines[1].UomId);
    }

    [Fact]
    public async Task Edit_Post_ClosedPo_RedirectsToDetail_NoServiceCall()
    {
        // Defence in depth — GET redirects Closed/Cancelled; POST does
        // too so a direct form submission cannot edit a terminal PO.
        var b = BuildAdmin();
        var baseDetail = SampleDetail("PO-CLOSED");
        var closedHeader = new PurchaseOrder
        {
            Id = baseDetail.Header.Id,
            PoNumber = baseDetail.Header.PoNumber,
            OwnerId = baseDetail.Header.OwnerId,
            WarehouseId = baseDetail.Header.WarehouseId,
            ExpectedDate = baseDetail.Header.ExpectedDate,
            Status = "Closed",
            Notes = baseDetail.Header.Notes,
        };
        var detail = new PurchaseOrderDetail(closedHeader, baseDetail.Lines);
        var poId = detail.Header.Id;
        b.Repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var vm = new PurchaseOrderEditViewModel { Id = poId, PoNumber = "PO-CLOSED" };
        var result = await b.Controller.Edit(poId, vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        b.Service.Verify(s => s.UpdatePartialAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<PartialUpdatePurchaseOrderRequest>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_Post_NewLineNumber_ClassifiedAsInsert()
    {
        // Posted LineNumber 2 doesn't exist in DB (only line 1) →
        // classified as insert.
        var b = BuildAdmin();
        var detail = SampleDetail("PO-INS", receivedLines: 0);  // 1 line, LineNumber=1
        var poId = detail.Header.Id;
        b.Repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        PartialUpdatePurchaseOrderRequest? captured = null;
        b.Service.Setup(s => s.UpdatePartialAsync(
                It.IsAny<Guid>(), poId, It.IsAny<PartialUpdatePurchaseOrderRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, PartialUpdatePurchaseOrderRequest, Guid?, CancellationToken>(
                (_, _, r, _, _) => captured = r)
            .ReturnsAsync(detail);

        var newProductId = Guid.NewGuid();
        var vm = new PurchaseOrderEditViewModel
        {
            Id = poId,
            PoNumber = "PO-INS",
            Lines = new List<PurchaseOrderLineViewModel>
            {
                new() { LineNumber = 1, ProductId = detail.Lines[0].ProductId,
                        UomId = detail.Lines[0].UomId, ExpectedQuantity = 10m },
                new() { LineNumber = 2, ProductId = newProductId,
                        UomId = Guid.NewGuid(), ExpectedQuantity = 5m },
            },
        };

        await b.Controller.Edit(poId, vm, default);

        Assert.NotNull(captured);
        Assert.Single(captured!.LineInserts);
        Assert.Equal(2, captured.LineInserts[0].LineNumber);
        Assert.Equal(newProductId, captured.LineInserts[0].ProductId);
    }

    [Fact]
    public async Task Edit_Post_MissingPostedLine_ClassifiedAsDelete()
    {
        // DB has line 1 (unlocked); posted Lines is empty → classified
        // as delete.
        var b = BuildAdmin();
        var detail = SampleDetail("PO-DEL", receivedLines: 0);
        var poId = detail.Header.Id;
        var dbLineId = detail.Lines[0].Id;
        b.Repo.Setup(r => r.GetByIdAsync(poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        PartialUpdatePurchaseOrderRequest? captured = null;
        b.Service.Setup(s => s.UpdatePartialAsync(
                It.IsAny<Guid>(), poId, It.IsAny<PartialUpdatePurchaseOrderRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, PartialUpdatePurchaseOrderRequest, Guid?, CancellationToken>(
                (_, _, r, _, _) => captured = r)
            .ReturnsAsync(detail);

        var vm = new PurchaseOrderEditViewModel
        {
            Id = poId,
            PoNumber = "PO-DEL",
            Lines = new List<PurchaseOrderLineViewModel>(),
        };

        await b.Controller.Edit(poId, vm, default);

        Assert.NotNull(captured);
        Assert.Empty(captured!.LineUpdates);
        Assert.Empty(captured.LineInserts);
        Assert.Single(captured.LineDeletes);
        Assert.Equal(dbLineId, captured.LineDeletes[0]);
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
