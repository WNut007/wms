using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Outbound;

namespace WMS.IntegrationTests.Controllers;

// Phase 14E — ShipmentsController tests. Same Build pattern as Phase
// 14D PackTasksControllerTests.
public class ShipmentsControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        ShipmentsController Controller,
        Mock<IShipmentRepository> ShipmentRepo,
        Mock<ICartonRepository> CartonRepo,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IShipmentService> Service,
        Mock<IValidator<CancelShipmentViewModel>> CancelValidator,
        Guid CurrentUserId);

    private static Build BuildController()
    {
        var shipmentRepo = new Mock<IShipmentRepository>();
        var shipmentFactory = new Mock<IShipmentRepositoryFactory>();
        shipmentFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(shipmentRepo.Object);

        var cartonRepo = new Mock<ICartonRepository>();
        cartonRepo.Setup(r => r.GetByShipmentIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Carton>());
        var cartonFactory = new Mock<ICartonRepositoryFactory>();
        cartonFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(cartonRepo.Object);

        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var service = new Mock<IShipmentService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);

        var cancelValidator = new Mock<IValidator<CancelShipmentViewModel>>();
        cancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelShipmentViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var ctrl = new ShipmentsController(
            shipmentFactory.Object, cartonFactory.Object, soFactory.Object,
            service.Object, tenant.Object, currentUser.Object, cancelValidator.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, shipmentRepo, cartonRepo, soRepo, service, cancelValidator, currentUserId);
    }

    private static Shipment NewShipment(Guid id, string status, Guid? soId = null) => new()
    {
        Id = id,
        ShipmentNumber = "SHP-X",
        SalesOrderId = soId ?? Guid.NewGuid(),
        Status = status,
        GeneratedAt = DateTime.UtcNow,
    };

    // ================================================================
    // Detail
    // ================================================================

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.ShipmentRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shipment?)null);

        var result = await b.Controller.Detail(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_PendingShipment_CanSubmitAndCancel()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.ShipmentRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(id, "Pending"));

        var result = await b.Controller.Detail(id, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(true, viewResult.ViewData["IsPending"]);
        Assert.Equal(false, viewResult.ViewData["IsTerminal"]);
        Assert.Equal(true, viewResult.ViewData["CanSubmit"]);
        Assert.Equal(true, viewResult.ViewData["CanCancel"]);
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public async Task Detail_TerminalShipment_CannotSubmitOrCancel(string status)
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.ShipmentRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(id, status));

        var result = await b.Controller.Detail(id, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(true, viewResult.ViewData["IsTerminal"]);
        Assert.Equal(false, viewResult.ViewData["CanSubmit"]);
        Assert.Equal(false, viewResult.ViewData["CanCancel"]);
    }

    // ================================================================
    // Submit
    // ================================================================

    [Fact]
    public async Task Submit_Happy_ProjectsRequest_RedirectsToDetail()
    {
        var b = BuildController();
        var id = Guid.NewGuid();

        b.Service.Setup(s => s.SubmitAsync(
                TenantId, It.IsAny<SubmitShipmentRequest>(), b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentSubmissionResult(
                ShipmentStatus: "Shipped",
                SalesOrderStatus: "Shipped",
                CartonCount: 1));

        var vm = new SubmitShipmentViewModel
        {
            Id = id,
            CarrierName = "Flash Express",
            TrackingNumber = "TRK-12345",
            Notes = "fragile",
        };

        var result = await b.Controller.Submit(id, vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal(id, redirect.RouteValues!["id"]);

        b.Service.Verify(s => s.SubmitAsync(
            TenantId,
            It.Is<SubmitShipmentRequest>(r => r.ShipmentId == id
                && r.CarrierName == "Flash Express"
                && r.TrackingNumber == "TRK-12345"
                && r.Notes == "fragile"),
            b.CurrentUserId,
            It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Contains("dispatched", b.Controller.TempData["ShipmentMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Submit_AllOptionalFieldsBlank_StillCallsService()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), It.IsAny<SubmitShipmentRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentSubmissionResult("Shipped", "Shipped", 0));

        var vm = new SubmitShipmentViewModel { Id = id };
        await b.Controller.Submit(id, vm, CancellationToken.None);

        // All-null fields flow through unchanged.
        b.Service.Verify(s => s.SubmitAsync(
            It.IsAny<Guid>(),
            It.Is<SubmitShipmentRequest>(r => r.CarrierName == null
                && r.TrackingNumber == null
                && r.Notes == null),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_ServiceThrows_TempDataError_RedirectsToDetail()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.SubmitAsync(
                It.IsAny<Guid>(), It.IsAny<SubmitShipmentRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("wrong state"));

        var vm = new SubmitShipmentViewModel { Id = id };
        var result = await b.Controller.Submit(id, vm, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("wrong state", b.Controller.TempData["ShipmentError"]);
    }

    // ================================================================
    // Cancel
    // ================================================================

    [Fact]
    public async Task Cancel_ValidationFails_TempDataError_NoServiceCall()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.CancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelShipmentViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Reason", "Reason must be at least 3 characters."),
            }));

        var vm = new CancelShipmentViewModel { Id = id, Reason = "x" };
        var result = await b.Controller.Cancel(id, vm, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("at least 3", b.Controller.TempData["ShipmentError"]?.ToString() ?? "");
        b.Service.Verify(s => s.CancelAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancel_Happy_CallsService_RouteIdWinsOverFormId()
    {
        var b = BuildController();
        var routeId = Guid.NewGuid();
        var formId = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                TenantId, routeId, "valid reason", b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new CancelShipmentViewModel { Id = formId, Reason = "valid reason" };
        await b.Controller.Cancel(routeId, vm, CancellationToken.None);

        b.Service.Verify(s => s.CancelAsync(
            TenantId, routeId, "valid reason", b.CurrentUserId,
            It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Contains("cancelled", b.Controller.TempData["ShipmentMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_IdempotentMessage()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Service.Setup(s => s.CancelAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new CancelShipmentViewModel { Id = id, Reason = "valid reason" };
        await b.Controller.Cancel(id, vm, CancellationToken.None);

        Assert.Contains("already cancelled",
            b.Controller.TempData["ShipmentMessage"]?.ToString() ?? "");
    }

    // ================================================================
    // Index + GetData (Phase 15A list page)
    // ================================================================

    [Fact]
    public void Index_ReturnsView()
    {
        var b = BuildController();
        var result = b.Controller.Index();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task GetData_Happy_ReturnsItemsAndCounts()
    {
        var b = BuildController();
        var rowId = Guid.NewGuid();
        b.ShipmentRepo.Setup(r => r.GetPagedAsync(
                It.IsAny<ShipmentFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WMS.DAL.Common.PagedResult<ShipmentListRow>
            {
                Items = new List<ShipmentListRow>
                {
                    new(rowId, "SHP-001", Guid.NewGuid(), "SO-001",
                        "CUST-A", "Cust A", "Pending",
                        CarrierName: "Flash Express",
                        TrackingNumber: "TRK-XYZ",
                        CartonCount: 1,
                        GeneratedAt: DateTime.UtcNow,
                        GeneratedByName: "Maya",
                        ShippedAt: null,
                        CancelledAt: null),
                },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1,
            });
        b.ShipmentRepo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<ShipmentFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentStatusCounts(
                All: 3, Pending: 1, Shipped: 1, Cancelled: 1));

        var result = Assert.IsType<JsonResult>(await b.Controller.GetData());
        var envelope = result.Value!;
        Assert.Equal(1, envelope.GetType().GetProperty("total")!.GetValue(envelope));

        var counts = envelope.GetType().GetProperty("counts")!.GetValue(envelope)!;
        Assert.Equal(3, counts.GetType().GetProperty("all")!.GetValue(counts));
        Assert.Equal(1, counts.GetType().GetProperty("shipped")!.GetValue(counts));
    }

    [Fact]
    public async Task GetData_StatusFilter_MappedToDb()
    {
        var b = BuildController();
        b.ShipmentRepo.Setup(r => r.GetPagedAsync(
                It.IsAny<ShipmentFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WMS.DAL.Common.PagedResult<ShipmentListRow>
            {
                Items = new(), Total = 0, Page = 1, PageSize = 20, TotalPages = 0,
            });
        b.ShipmentRepo.Setup(r => r.GetStatusCountsAsync(
                It.IsAny<ShipmentFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentStatusCounts(0, 0, 0, 0));

        await b.Controller.GetData(status: "shipped");

        b.ShipmentRepo.Verify(r => r.GetPagedAsync(
            It.Is<ShipmentFilter>(f => f.Status == "Shipped"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
