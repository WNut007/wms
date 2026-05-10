using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;

namespace WMS.UnitTests.Services.Outbound;

// Phase 14D — PackTaskService unit tests. Same pattern as Phase 14C
// PickTaskServiceTests: TX wrapping is not asserted directly
// (integration concern, TD-006 family); tests verify the right repo
// calls happen with the right arguments + the right state-transition
// decisions.
public class PackTaskServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid UomId = Guid.NewGuid();

    private record Build(
        PackTaskService Service,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IPackTaskRepository> PackRepo,
        Mock<ICartonRepository> CartonRepo);

    private static Build NewService()
    {
        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var packRepo = new Mock<IPackTaskRepository>();
        var packFactory = new Mock<IPackTaskRepositoryFactory>();
        packFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(packRepo.Object);

        var cartonRepo = new Mock<ICartonRepository>();
        var cartonFactory = new Mock<ICartonRepositoryFactory>();
        cartonFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(cartonRepo.Object);

        var service = new PackTaskService(
            soFactory.Object,
            packFactory.Object,
            cartonFactory.Object,
            NullLogger<PackTaskService>.Instance);

        return new Build(service, soRepo, packRepo, cartonRepo);
    }

    private static SalesOrder NewSoHeader(Guid id, string status) => new()
    {
        Id = id,
        SoNumber = "SO-X",
        WarehouseId = Guid.NewGuid(),
        Status = status,
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static SalesOrderLine NewSoLine(
        Guid id, Guid soId, decimal ordered, decimal picked = 0m) => new()
    {
        Id = id, SalesOrderId = soId, LineNumber = 1,
        ProductId = ProductId, OwnerId = OwnerId, UomId = UomId,
        OrderedQuantity = ordered,
        PickedQuantity = picked,
    };

    private static PackTask NewPackHeader(Guid id, string status, Guid soId) => new()
    {
        Id = id,
        PackNumber = "PACK-X",
        SalesOrderId = soId,
        Status = status,
    };

    private static PackTaskLine NewPackLine(
        Guid id, Guid taskId, Guid soLineId, decimal picked) => new()
    {
        Id = id,
        PackTaskId = taskId,
        SalesOrderLineId = soLineId,
        ProductId = ProductId, OwnerId = OwnerId, UomId = UomId,
        PickedQuantity = picked,
        LineStatus = "Pending",
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
    [InlineData("Allocating")]
    [InlineData("Allocated")]
    [InlineData("Picking")]
    [InlineData("Packed")]
    [InlineData("Cancelled")]
    public async Task Generate_WrongState_Throws(string status)
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, status), new List<SalesOrderLine>()));
        b.PackRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackTask?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Generate_ExistingPendingTask_ReturnsExisting()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        var existingTaskId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, "Picked"), new List<SalesOrderLine>()));

        var existing = NewPackHeader(existingTaskId, "Pending", soId);
        existing.PackNumber = "PACK-EXISTING";
        b.PackRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        b.PackRepo.Setup(r => r.GetByIdAsync(existingTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(existing,
                new List<PackTaskLine> { NewPackLine(Guid.NewGuid(), existingTaskId, Guid.NewGuid(), 5m) },
                Carton: null));

        var result = await b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid());

        Assert.Equal(existingTaskId, result.PackTaskId);
        Assert.Equal("PACK-EXISTING", result.PackNumber);
        // Idempotent: no INSERT path called.
        b.PackRepo.Verify(r => r.CreateAsync(
            It.IsAny<PackTask>(), It.IsAny<IReadOnlyList<PackTaskLine>>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Generate_PickedButNoPositivelyPickedLines_Throws()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewSoHeader(soId, "Picked"),
                new List<SalesOrderLine> { NewSoLine(Guid.NewGuid(), soId, 10m, picked: 0m) }));
        b.PackRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackTask?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Generate_Happy_NoSoFlip_InsertsTask_SnapshotPickedLines()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewSoHeader(soId, "PartiallyPicked"),
                new List<SalesOrderLine>
                {
                    NewSoLine(lineId, soId, 10m, picked: 7m),
                    NewSoLine(Guid.NewGuid(), soId, 5m, picked: 0m),  // skipped on pick — won't spawn pack line
                }));
        b.PackRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackTask?)null);
        b.PackRepo.Setup(r => r.CountForDatePrefixAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await b.Service.GenerateAsync(TenantId, soId, userId);

        Assert.StartsWith($"PACK-{DateTime.UtcNow:yyyyMMdd}-", result.PackNumber);
        Assert.EndsWith("-0001", result.PackNumber);
        Assert.Equal(1, result.LineCount);     // Only 1 line — the zero-pick line skipped
        Assert.Equal(7m, result.TotalPickedQuantity);

        // No SO state flip on Generate.
        b.SoRepo.Verify(r => r.SetStatusAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        b.PackRepo.Verify(r => r.CreateAsync(
            It.Is<PackTask>(h => h.SalesOrderId == soId && h.Status == "Pending"),
            It.Is<IReadOnlyList<PackTaskLine>>(lines =>
                lines.Count == 1
                && lines[0].SalesOrderLineId == lineId
                && lines[0].PickedQuantity == 7m
                && lines[0].LineStatus == "Pending"),
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
        var req = new SubmitPackTaskRequest(Guid.NewGuid(), Array.Empty<PackedLineEntry>(),
            BoxTypeId: null, WeightKg: null, CartonNotes: null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("Packed")]
    [InlineData("Cancelled")]
    public async Task Submit_TerminalState_Throws(string status)
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, status, Guid.NewGuid()),
                new List<PackTaskLine>(),
                Carton: null));

        var req = new SubmitPackTaskRequest(taskId, Array.Empty<PackedLineEntry>(),
            null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_EmptyRequest_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PackTaskLine>
                {
                    NewPackLine(Guid.NewGuid(), taskId, Guid.NewGuid(), 5m),
                },
                Carton: null));

        var req = new SubmitPackTaskRequest(taskId, Array.Empty<PackedLineEntry>(),
            null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_MissingLine_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var line1 = Guid.NewGuid();
        var line2 = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PackTaskLine>
                {
                    NewPackLine(line1, taskId, Guid.NewGuid(), 5m),
                    NewPackLine(line2, taskId, Guid.NewGuid(), 5m),
                },
                Carton: null));

        var req = new SubmitPackTaskRequest(taskId, new[]
        {
            new PackedLineEntry(line1, 5m, "Packed", null, null),
        }, null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_PackedShortNoReason_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PackTaskLine> { NewPackLine(lineId, taskId, Guid.NewGuid(), 5m) },
                Carton: null));

        var req = new SubmitPackTaskRequest(taskId, new[]
        {
            new PackedLineEntry(lineId, 3m, "Packed", ShortPackReason: null, Notes: null),
        }, null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_SkippedNoReason_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PackTaskLine> { NewPackLine(lineId, taskId, Guid.NewGuid(), 5m) },
                Carton: null));

        var req = new SubmitPackTaskRequest(taskId, new[]
        {
            new PackedLineEntry(lineId, null, "Skipped", ShortPackReason: "  ", Notes: null),
        }, null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_PackedQtyOverPicked_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PackTaskLine> { NewPackLine(lineId, taskId, Guid.NewGuid(), 5m) },
                Carton: null));

        var req = new SubmitPackTaskRequest(taskId, new[]
        {
            new PackedLineEntry(lineId, 10m, "Packed", null, null),
        }, null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_NegativeWeight_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PackTaskLine> { NewPackLine(lineId, taskId, Guid.NewGuid(), 5m) },
                Carton: null));

        var req = new SubmitPackTaskRequest(taskId, new[]
        {
            new PackedLineEntry(lineId, 5m, "Packed", null, null),
        }, BoxTypeId: null, WeightKg: -1m, CartonNotes: null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_FullPack_FlipsTaskPacked_FlipsSoPacked_CreatesCarton()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var soLineId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", soId),
                new List<PackTaskLine> { NewPackLine(lineId, taskId, soLineId, 5m) },
                Carton: null));

        b.PackRepo.Setup(r => r.SetPackedAsync(taskId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Picked", "Packed", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.CartonRepo.Setup(r => r.CountForDatePrefixAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var req = new SubmitPackTaskRequest(taskId, new[]
        {
            new PackedLineEntry(lineId, 5m, "Packed", null, null),
        }, BoxTypeId: null, WeightKg: 1.5m, CartonNotes: "first carton");

        var result = await b.Service.SubmitAsync(TenantId, req, userId);

        Assert.Equal("Packed", result.TaskStatus);
        Assert.Equal("Packed", result.SalesOrderStatus);
        Assert.Equal(1, result.FullyPackedLineCount);
        Assert.Equal(0, result.ShortPackedLineCount);
        Assert.Equal(0, result.SkippedLineCount);
        Assert.Equal(5m, result.TotalPackedQuantity);
        Assert.StartsWith($"CTN-{DateTime.UtcNow:yyyyMMdd}-", result.CartonNumber);

        // Per-line UPDATE called.
        b.PackRepo.Verify(r => r.UpdateLinePackedAsync(
            lineId, 5m, "Packed", null, null, userId, It.IsAny<CancellationToken>()),
            Times.Once);

        // Carton INSERT called with the operator-supplied metadata.
        b.CartonRepo.Verify(r => r.CreateAsync(
            It.Is<Carton>(c =>
                c.PackTaskId == taskId
                && c.WeightKg == 1.5m
                && c.Notes == "first carton"
                && c.BoxTypeId == null),
            userId, It.IsAny<CancellationToken>()),
            Times.Once);

        // SO flipped Picked → Packed.
        b.SoRepo.Verify(r => r.SetStatusAsync(
            soId, "Picked", "Packed", userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_FullPack_FromPartiallyPicked_FlipsToPacked()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", soId),
                new List<PackTaskLine> { NewPackLine(lineId, taskId, Guid.NewGuid(), 3m) },
                Carton: null));

        b.PackRepo.Setup(r => r.SetPackedAsync(taskId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // First || branch fails (SO is PartiallyPicked, not Picked).
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Picked", "Packed", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // Second || branch succeeds.
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "PartiallyPicked", "Packed", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new SubmitPackTaskRequest(taskId, new[]
        {
            new PackedLineEntry(lineId, 3m, "Packed", null, null),
        }, null, null, null);

        var result = await b.Service.SubmitAsync(TenantId, req, userId);

        Assert.Equal("Packed", result.SalesOrderStatus);
        b.SoRepo.Verify(r => r.SetStatusAsync(
            soId, "PartiallyPicked", "Packed", userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_Skipped_NoCartonContents_StillCreatesCarton()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", soId),
                new List<PackTaskLine> { NewPackLine(lineId, taskId, Guid.NewGuid(), 5m) },
                Carton: null));

        b.PackRepo.Setup(r => r.SetPackedAsync(taskId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Picked", "Packed", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new SubmitPackTaskRequest(taskId, new[]
        {
            new PackedLineEntry(lineId, null, "Skipped", "Damaged", null),
        }, null, null, null);

        var result = await b.Service.SubmitAsync(TenantId, req, userId);

        Assert.Equal(1, result.SkippedLineCount);
        Assert.Equal(0m, result.TotalPackedQuantity);

        // Skipped: line update writes null qty + 'Skipped' status.
        b.PackRepo.Verify(r => r.UpdateLinePackedAsync(
            lineId, null, "Skipped", "Damaged", null, userId, It.IsAny<CancellationToken>()),
            Times.Once);

        // Carton STILL created (empty cartons are fine for the audit trail —
        // operator might have packed nothing but still sealed the box).
        b.CartonRepo.Verify(r => r.CreateAsync(
            It.IsAny<Carton>(), userId, It.IsAny<CancellationToken>()),
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
        var taskId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Cancelled", Guid.NewGuid()),
                new List<PackTaskLine>(), Carton: null));

        var changed = await b.Service.CancelAsync(TenantId, taskId, "any", Guid.NewGuid());
        Assert.False(changed);

        b.PackRepo.Verify(r => r.SetCancelledAsync(
            It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancel_PostSubmitPacked_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Packed", Guid.NewGuid()),
                new List<PackTaskLine>(), Carton: null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.CancelAsync(TenantId, taskId, "any", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_Happy_FlipsTaskCancelled_NoSoFlip()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.PackRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTaskDetail(
                NewPackHeader(taskId, "Pending", soId),
                new List<PackTaskLine>(), Carton: null));
        b.PackRepo.Setup(r => r.SetCancelledAsync(
                taskId, "needed elsewhere", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var changed = await b.Service.CancelAsync(TenantId, taskId, "needed elsewhere", userId);

        Assert.True(changed);
        b.PackRepo.Verify(r => r.SetCancelledAsync(
            taskId, "needed elsewhere", userId, It.IsAny<CancellationToken>()),
            Times.Once);

        // No SO state flip — Generate didn't flip it, so Cancel doesn't either.
        b.SoRepo.Verify(r => r.SetStatusAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
