using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.UnitTests.Services.Outbound;

// Phase 14E — ShipmentService unit tests. Mirrors Phase 14D Pack-
// TaskServiceTests pattern.
public class ShipmentServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        ShipmentService Service,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IShipmentRepository> ShipmentRepo,
        Mock<ICartonRepository> CartonRepo);

    private static Build NewService()
    {
        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var shipmentRepo = new Mock<IShipmentRepository>();
        var shipmentFactory = new Mock<IShipmentRepositoryFactory>();
        shipmentFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(shipmentRepo.Object);

        var cartonRepo = new Mock<ICartonRepository>();
        var cartonFactory = new Mock<ICartonRepositoryFactory>();
        cartonFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(cartonRepo.Object);

        var service = new ShipmentService(
            soFactory.Object,
            shipmentFactory.Object,
            cartonFactory.Object,
            NullLogger<ShipmentService>.Instance);

        return new Build(service, soRepo, shipmentRepo, cartonRepo);
    }

    private static SalesOrder NewSoHeader(Guid id, string status) => new()
    {
        Id = id,
        SoNumber = "SO-X",
        WarehouseId = Guid.NewGuid(),
        Status = status,
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static Shipment NewShipment(Guid id, string status, Guid soId) => new()
    {
        Id = id,
        ShipmentNumber = "SHP-X",
        SalesOrderId = soId,
        Status = status,
    };

    // ================================================================
    // GenerateAsync
    // ================================================================

    [Fact]
    public async Task Generate_SoNotFound_Throws()
    {
        var b = NewService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.GenerateAsync(TenantId, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Open")]
    [InlineData("Allocated")]
    [InlineData("Picking")]
    [InlineData("Picked")]
    [InlineData("PartiallyPicked")]
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public async Task Generate_WrongState_Throws(string status)
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, status), new List<SalesOrderLine>()));
        b.ShipmentRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shipment?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Generate_ExistingPendingShipment_ReturnsExisting()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        var existingId = Guid.NewGuid();

        // SO state doesn't matter — existing-task guard short-circuits
        // before the state check. Mirrors Pack/Pick precedent.
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, "Packed"), new List<SalesOrderLine>()));

        var existing = NewShipment(existingId, "Pending", soId);
        existing.ShipmentNumber = "SHP-EXISTING";
        b.ShipmentRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid());

        Assert.Equal(existingId, result.ShipmentId);
        Assert.Equal("SHP-EXISTING", result.ShipmentNumber);
        // Idempotent: no INSERT.
        b.ShipmentRepo.Verify(r => r.CreateAsync(
            It.IsAny<Shipment>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Generate_Happy_NoSoFlip_InsertsShipment()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, "Packed"), new List<SalesOrderLine>()));
        b.ShipmentRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shipment?)null);
        b.ShipmentRepo.Setup(r => r.CountForDatePrefixAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await b.Service.GenerateAsync(TenantId, soId, userId);

        Assert.StartsWith($"SHP-{DateTime.UtcNow:yyyyMMdd}-", result.ShipmentNumber);
        Assert.EndsWith("-0001", result.ShipmentNumber);

        // No SO state flip on Generate.
        b.SoRepo.Verify(r => r.SetStatusAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        b.ShipmentRepo.Verify(r => r.CreateAsync(
            It.Is<Shipment>(h => h.SalesOrderId == soId
                && h.Status == "Pending"
                && h.ShipmentNumber == result.ShipmentNumber),
            userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // SubmitAsync
    // ================================================================

    [Fact]
    public async Task Submit_NotFound_Throws()
    {
        var b = NewService();
        var req = new SubmitShipmentRequest(Guid.NewGuid(), null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public async Task Submit_TerminalState_Throws(string status)
    {
        var b = NewService();
        var shipmentId = Guid.NewGuid();
        b.ShipmentRepo.Setup(r => r.GetByIdAsync(shipmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(shipmentId, status, Guid.NewGuid()));

        var req = new SubmitShipmentRequest(shipmentId, null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_Happy_FlipsShipment_StampsCartons_FlipsSo()
    {
        var b = NewService();
        var shipmentId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.ShipmentRepo.Setup(r => r.GetByIdAsync(shipmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(shipmentId, "Pending", soId));
        b.ShipmentRepo.Setup(r => r.SetShippedAsync(
                shipmentId, "Flash Express", "TRK-12345", "fragile",
                userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.CartonRepo.Setup(r => r.StampShipmentForSalesOrderAsync(
                soId, shipmentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);  // 2 cartons stamped (e.g. multi-pack edge case)
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Packed", "Shipped", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new SubmitShipmentRequest(
            shipmentId, "  Flash Express  ", "  TRK-12345  ", "  fragile  ");

        var result = await b.Service.SubmitAsync(TenantId, req, userId);

        Assert.Equal("Shipped", result.ShipmentStatus);
        Assert.Equal("Shipped", result.SalesOrderStatus);
        Assert.Equal(2, result.CartonCount);

        // Verify SetShippedAsync got TRIMMED inputs (operator may
        // paste with whitespace).
        b.ShipmentRepo.Verify(r => r.SetShippedAsync(
            shipmentId, "Flash Express", "TRK-12345", "fragile",
            userId, It.IsAny<CancellationToken>()),
            Times.Once);

        b.CartonRepo.Verify(r => r.StampShipmentForSalesOrderAsync(
            soId, shipmentId, userId, It.IsAny<CancellationToken>()),
            Times.Once);

        b.SoRepo.Verify(r => r.SetStatusAsync(
            soId, "Packed", "Shipped", userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_AllOptionalFieldsNull_StillFlipsToShipped()
    {
        var b = NewService();
        var shipmentId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.ShipmentRepo.Setup(r => r.GetByIdAsync(shipmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(shipmentId, "Pending", soId));
        b.ShipmentRepo.Setup(r => r.SetShippedAsync(
                shipmentId, null, null, null,
                userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Packed", "Shipped", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new SubmitShipmentRequest(shipmentId, null, null, null);

        var result = await b.Service.SubmitAsync(TenantId, req, userId);

        Assert.Equal("Shipped", result.ShipmentStatus);
        // All-null inputs flow through as null (no whitespace
        // normalisation issue).
        b.ShipmentRepo.Verify(r => r.SetShippedAsync(
            shipmentId, null, null, null, userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_LongCarrierName_TruncatedTo50()
    {
        var b = NewService();
        var shipmentId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var longName = new string('A', 75);
        var expectedTrunc = new string('A', 50);

        b.ShipmentRepo.Setup(r => r.GetByIdAsync(shipmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(shipmentId, "Pending", soId));
        b.ShipmentRepo.Setup(r => r.SetShippedAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.SoRepo.Setup(r => r.SetStatusAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new SubmitShipmentRequest(shipmentId, longName, null, null);

        await b.Service.SubmitAsync(TenantId, req, Guid.NewGuid());

        b.ShipmentRepo.Verify(r => r.SetShippedAsync(
            shipmentId, expectedTrunc, null, null,
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // CancelAsync
    // ================================================================

    [Fact]
    public async Task Cancel_BlankReason_Throws()
    {
        var b = NewService();
        await Assert.ThrowsAsync<ArgumentException>(
            () => b.Service.CancelAsync(TenantId, Guid.NewGuid(), "  ", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_NotFound_Throws()
    {
        var b = NewService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.CancelAsync(TenantId, Guid.NewGuid(), "valid", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_Idempotent_ReturnsFalse()
    {
        var b = NewService();
        var shipmentId = Guid.NewGuid();
        b.ShipmentRepo.Setup(r => r.GetByIdAsync(shipmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(shipmentId, "Cancelled", Guid.NewGuid()));

        var changed = await b.Service.CancelAsync(TenantId, shipmentId, "any", Guid.NewGuid());
        Assert.False(changed);

        b.ShipmentRepo.Verify(r => r.SetCancelledAsync(
            It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancel_PostSubmitShipped_Throws()
    {
        var b = NewService();
        var shipmentId = Guid.NewGuid();
        b.ShipmentRepo.Setup(r => r.GetByIdAsync(shipmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(shipmentId, "Shipped", Guid.NewGuid()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.CancelAsync(TenantId, shipmentId, "any", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_Happy_FlipsShipment_NoSoFlip()
    {
        var b = NewService();
        var shipmentId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.ShipmentRepo.Setup(r => r.GetByIdAsync(shipmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewShipment(shipmentId, "Pending", soId));
        b.ShipmentRepo.Setup(r => r.SetCancelledAsync(
                shipmentId, "no carrier showed", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var changed = await b.Service.CancelAsync(TenantId, shipmentId, "no carrier showed", userId);

        Assert.True(changed);
        b.ShipmentRepo.Verify(r => r.SetCancelledAsync(
            shipmentId, "no carrier showed", userId, It.IsAny<CancellationToken>()),
            Times.Once);

        // No SO state flip — Generate didn't flip it.
        b.SoRepo.Verify(r => r.SetStatusAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
