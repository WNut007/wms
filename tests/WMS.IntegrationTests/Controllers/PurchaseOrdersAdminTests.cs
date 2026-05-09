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

        var docs = Mock.Of<IDocumentStorageService>();

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
            ctrl, repo, service, ownerRepo, productRepo, uomRepo, warehouseRepo,
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
}
