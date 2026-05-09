using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
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

// Phase 9B controller tests — focused on the dual-button submit flow
// (Save Draft vs Post Receipt) and PO line pre-fill JSON endpoint.
public class GoodsReceiptControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        GoodsReceiptController Controller,
        Mock<IReceivingHeaderService> Service,
        Mock<IPurchaseOrderRepository> PoRepo,
        Mock<IValidator<GoodsReceiptCreateViewModel>> Validator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var service     = new Mock<IReceivingHeaderService>();
        var poRepo      = new Mock<IPurchaseOrderRepository>();
        var poFactory   = new Mock<IPurchaseOrderRepositoryFactory>();
        poFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(poRepo.Object);

        var warehouseRepo = new Mock<IWarehouseRepository>();
        warehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo>
            {
                new(Guid.NewGuid(), "WH-MAIN", "Main Warehouse"),
            });
        var warehouseFactory = new Mock<IWarehouseRepositoryFactory>();
        warehouseFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(warehouseRepo.Object);

        var ownerRepo = new Mock<IOwnerRepository>();
        ownerRepo.Setup(r => r.GetActiveSuppliersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LookupItem>());
        var ownerFactory = new Mock<IOwnerRepositoryFactory>();
        ownerFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(ownerRepo.Object);

        var productRepo = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LookupItem>());
        var productFactory = new Mock<IProductRepositoryFactory>();
        productFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(productRepo.Object);

        var uomRepo = new Mock<IUomRepository>();
        uomRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LookupItem>());
        var uomFactory = new Mock<IUomRepositoryFactory>();
        uomFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(uomRepo.Object);

        var locationRepo = new Mock<ILocationRepository>();
        locationRepo.Setup(r => r.GetActiveByWarehouseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LookupItem>());
        var locationFactory = new Mock<ILocationRepositoryFactory>();
        locationFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(locationRepo.Object);

        // Default empty PO list for the "Open POs" picker.
        poRepo.Setup(r => r.GetPagedAsync(It.IsAny<PurchaseOrderFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PurchaseOrderListRow>
            {
                Items = new(), Total = 0, Page = 1, PageSize = 200,
            });

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);
        currentUser.SetupGet(u => u.WarehouseId).Returns((Guid?)null);

        var validator = new Mock<IValidator<GoodsReceiptCreateViewModel>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<GoodsReceiptCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var ctrl = new GoodsReceiptController(
            service.Object, poFactory.Object, warehouseFactory.Object,
            ownerFactory.Object, productFactory.Object, uomFactory.Object,
            locationFactory.Object, tenant.Object, currentUser.Object,
            validator.Object, NullLogger<GoodsReceiptController>.Instance);

        return new AdminBuild(ctrl, service, poRepo, validator, userId);
    }

    private static PurchaseOrderDetail SamplePoDetail()
    {
        var poId = Guid.NewGuid();
        var lines = new List<PurchaseOrderLine>
        {
            new()
            {
                Id = Guid.NewGuid(), PurchaseOrderId = poId, LineNumber = 1,
                ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                ExpectedQuantity = 10m, ReceivedQuantity = 0m, Status = "Open",
            },
            new()
            {
                Id = Guid.NewGuid(), PurchaseOrderId = poId, LineNumber = 2,
                ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                ExpectedQuantity = 5m, ReceivedQuantity = 5m, Status = "Closed",
            },
        };
        var header = new PurchaseOrder
        {
            Id = poId, PoNumber = "PO-A",
            OwnerId = Guid.NewGuid(), WarehouseId = Guid.NewGuid(),
            Status = "Open",
        };
        return new PurchaseOrderDetail(header, lines);
    }

    [Fact]
    public async Task Create_Get_DefaultStarter_PopulatesOneEmptyLine()
    {
        var b = BuildAdmin();
        var result = await b.Controller.Create(poId: null, default);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<GoodsReceiptCreateViewModel>(view.Model);
        Assert.Single(vm.Lines);
        Assert.False(string.IsNullOrEmpty(vm.ReceivingNumber));
        Assert.StartsWith("GR-", vm.ReceivingNumber);
    }

    [Fact]
    public async Task Create_Get_WithPoId_PrefillsLines_OpenAndPartialOnly()
    {
        var b = BuildAdmin();
        var detail = SamplePoDetail();
        b.PoRepo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await b.Controller.Create(poId: detail.Header.Id, default);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<GoodsReceiptCreateViewModel>(view.Model);
        // Only the Open line should pre-fill (the Closed line is excluded).
        Assert.Single(vm.Lines);
        Assert.Equal(1, vm.Lines[0].LineNumber);
        Assert.Equal(detail.Header.Id, vm.PurchaseOrderId);
        Assert.Equal("PO-A", vm.PoNumber);
    }

    [Fact]
    public async Task Create_Post_Draft_SetsIsDraftTrue()
    {
        var b = BuildAdmin();
        PostReceivingRequest? captured = null;
        b.Service.Setup(s => s.PostReceivingAsync(
                It.IsAny<Guid>(), It.IsAny<PostReceivingRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, PostReceivingRequest, Guid?, CancellationToken>((_, r, _, _) => captured = r)
            .ReturnsAsync(MakeDetail());

        var vm = MakeValidVm();
        var result = await b.Controller.Create(vm, action: "draft", default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(captured);
        Assert.True(captured!.IsDraft);
    }

    [Fact]
    public async Task Create_Post_PostReceipt_SetsIsDraftFalse()
    {
        var b = BuildAdmin();
        PostReceivingRequest? captured = null;
        b.Service.Setup(s => s.PostReceivingAsync(
                It.IsAny<Guid>(), It.IsAny<PostReceivingRequest>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, PostReceivingRequest, Guid?, CancellationToken>((_, r, _, _) => captured = r)
            .ReturnsAsync(MakeDetail());

        var vm = MakeValidVm();
        var result = await b.Controller.Create(vm, action: "post", default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(captured);
        Assert.False(captured!.IsDraft);
    }

    [Fact]
    public async Task Create_Post_FluentValidationFails_ReturnsView()
    {
        var b = BuildAdmin();
        b.Validator
            .Setup(v => v.ValidateAsync(It.IsAny<GoodsReceiptCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("ReceivingNumber", "Already in use."),
            }));

        var vm = MakeValidVm();
        var result = await b.Controller.Create(vm, action: "post", default);

        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
    }

    [Fact]
    public async Task PoLines_Endpoint_ReturnsOpenLinesOnly()
    {
        var b = BuildAdmin();
        var detail = SamplePoDetail();
        b.PoRepo.Setup(r => r.GetByIdAsync(detail.Header.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await b.Controller.PoLines(detail.Header.Id, default);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        // Use reflection to inspect the anonymous-typed payload.
        var payloadType = json.Value!.GetType();
        var lines = payloadType.GetProperty("lines")!.GetValue(json.Value);
        Assert.NotNull(lines);
        var list = (System.Collections.IEnumerable)lines!;
        var count = list.Cast<object>().Count();
        // Only the Open line; the Closed line (line 2) is filtered out.
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PoLines_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.PoRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderDetail?)null);

        var result = await b.Controller.PoLines(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    private static GoodsReceiptCreateViewModel MakeValidVm() => new()
    {
        ReceivingNumber = "GR-TEST-001",
        ReceivedAt = DateTime.UtcNow,
        WarehouseId = Guid.NewGuid(),
        Lines = new List<GoodsReceiptLineViewModel>
        {
            new()
            {
                LineNumber = 1, ProductId = Guid.NewGuid(), UomId = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(), LocationId = Guid.NewGuid(),
                ReceivedQuantity = 5m,
            },
        },
    };

    private static ReceivingDetail MakeDetail() =>
        new(new ReceivingHeader { Id = Guid.NewGuid(), ReceivingNumber = "GR-TEST", WarehouseId = Guid.NewGuid(), Status = "Posted" },
            new List<ReceivingLine>());
}
