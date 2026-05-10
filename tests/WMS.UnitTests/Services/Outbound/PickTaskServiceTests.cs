using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Inventory;
using WMS.Domain.Entities.Outbound;

namespace WMS.UnitTests.Services.Outbound;

// Phase 14C — PickTaskService unit tests. Mirrors AllocationServiceTests
// patterns: TX wrapping is not asserted directly (integration-test
// concern in TD-006 family); tests verify the right repo calls happen
// with the right arguments and the right state-transition decisions.
public class PickTaskServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid UomId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();

    private record Build(
        PickTaskService Service,
        Mock<ISalesOrderRepository> SoRepo,
        Mock<IOrderAllocationRepository> AllocRepo,
        Mock<IStockRepository> StockRepo,
        Mock<IPickTaskRepository> PickRepo);

    private static Build NewService()
    {
        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var allocRepo = new Mock<IOrderAllocationRepository>();
        var allocFactory = new Mock<IOrderAllocationRepositoryFactory>();
        allocFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(allocRepo.Object);

        var stockRepo = new Mock<IStockRepository>();
        var stockFactory = new Mock<IStockRepositoryFactory>();
        stockFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(stockRepo.Object);

        var pickRepo = new Mock<IPickTaskRepository>();
        var pickFactory = new Mock<IPickTaskRepositoryFactory>();
        pickFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(pickRepo.Object);

        var service = new PickTaskService(
            soFactory.Object,
            allocFactory.Object,
            stockFactory.Object,
            pickFactory.Object,
            NullLogger<PickTaskService>.Instance);

        return new Build(service, soRepo, allocRepo, stockRepo, pickRepo);
    }

    private static SalesOrder NewSoHeader(Guid id, string status) => new()
    {
        Id = id,
        SoNumber = "SO-X",
        WarehouseId = WarehouseId,
        Status = status,
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static SalesOrderLine NewSoLine(
        Guid id, Guid soId, decimal ordered,
        decimal allocated = 0m, decimal picked = 0m) => new()
    {
        Id = id, SalesOrderId = soId, LineNumber = 1,
        ProductId = ProductId, OwnerId = OwnerId, UomId = UomId,
        OrderedQuantity = ordered,
        AllocatedQuantity = allocated,
        PickedQuantity = picked,
    };

    private static OrderAllocation NewAllocation(
        Guid id, Guid lineId, Guid stockId, decimal qty) => new()
    {
        Id = id,
        SalesOrderLineId = lineId,
        StockId = stockId,
        AllocatedQuantity = qty,
        Status = "Active",
    };

    private static Stock NewStock(Guid id) => new()
    {
        Id = id,
        LocationId = LocationId,
        ProductId = ProductId,
        OwnerId = OwnerId,
        UomId = UomId,
    };

    private static PickTask NewPickHeader(Guid id, string status, Guid soId) => new()
    {
        Id = id,
        PickNumber = "PICK-X",
        SalesOrderId = soId,
        Status = status,
    };

    private static PickTaskLine NewPickLine(
        Guid id, Guid taskId, Guid allocId, Guid stockId,
        decimal expected, string status = "Pending") => new()
    {
        Id = id,
        PickTaskId = taskId,
        OrderAllocationId = allocId,
        StockId = stockId,
        LocationId = LocationId,
        ProductId = ProductId, OwnerId = OwnerId, UomId = UomId,
        ExpectedQuantity = expected,
        LineStatus = status,
    };

    // ================================================================
    // GenerateAsync — state gating
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
    [InlineData("Cancelled")]
    [InlineData("Picked")]
    [InlineData("PartiallyPicked")]
    public async Task Generate_WrongState_Throws(string status)
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, status), new List<SalesOrderLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Generate_AlreadyPicking_ReturnsExistingTask()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        var existingTaskId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, "Picking"), new List<SalesOrderLine>()));

        var existingHeader = NewPickHeader(existingTaskId, "InProgress", soId);
        existingHeader.PickNumber = "PICK-EXISTING";
        b.PickRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingHeader);
        b.PickRepo.Setup(r => r.GetByIdAsync(existingTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(existingHeader, new List<PickTaskLine>
            {
                NewPickLine(Guid.NewGuid(), existingTaskId, Guid.NewGuid(), Guid.NewGuid(), expected: 5m),
            }));

        var result = await b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid());

        Assert.Equal(existingTaskId, result.PickTaskId);
        Assert.Equal("PICK-EXISTING", result.PickNumber);
        Assert.Equal(1, result.LineCount);
        // Idempotent: no INSERT path called.
        b.PickRepo.Verify(r => r.CreateAsync(
            It.IsAny<PickTask>(), It.IsAny<IReadOnlyList<PickTaskLine>>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Generate_AllocatedButNoActiveAllocations_Throws()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, "Allocated"), new List<SalesOrderLine>()));
        b.PickRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PickTask?)null);
        b.AllocRepo.Setup(r => r.GetActiveEntitiesBySalesOrderIdAsync(
                soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderAllocation>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Generate_AllocatedWithActiveTask_Throws()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(NewSoHeader(soId, "Allocated"), new List<SalesOrderLine>()));
        // Defensive: SO=Allocated should have NO active task; if one
        // exists it's a corrupt state — service throws.
        b.PickRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPickHeader(Guid.NewGuid(), "Pending", soId));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.GenerateAsync(TenantId, soId, Guid.NewGuid()));
    }

    // ================================================================
    // GenerateAsync — happy path
    // ================================================================

    [Fact]
    public async Task Generate_Happy_FlipsSoToPicking_InsertsTask_SnapshotLines()
    {
        var b = NewService();
        var soId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewSoHeader(soId, "Allocated"),
                new List<SalesOrderLine> { NewSoLine(lineId, soId, 10m, allocated: 10m) }));
        b.PickRepo.Setup(r => r.GetActiveBySalesOrderAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PickTask?)null);
        b.AllocRepo.Setup(r => r.GetActiveEntitiesBySalesOrderIdAsync(
                soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderAllocation>
            {
                NewAllocation(allocId, lineId, stockId, qty: 10m),
            });
        b.StockRepo.Setup(r => r.GetByIdAsync(stockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewStock(stockId));
        b.PickRepo.Setup(r => r.CountForDatePrefixAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Allocated", "Picking", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.GenerateAsync(TenantId, soId, userId);

        Assert.StartsWith($"PICK-{DateTime.UtcNow:yyyyMMdd}-", result.PickNumber);
        Assert.EndsWith("-0004", result.PickNumber); // CountForDatePrefix=3 → +1 → 4
        Assert.Equal(1, result.LineCount);
        Assert.Equal(10m, result.TotalExpectedQuantity);

        // Verify SO flipped Allocated → Picking.
        b.SoRepo.Verify(r => r.SetStatusAsync(
            soId, "Allocated", "Picking", userId, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify task header + lines snapshot is correct.
        b.PickRepo.Verify(r => r.CreateAsync(
            It.Is<PickTask>(h =>
                h.SalesOrderId == soId
                && h.Status == "Pending"
                && h.PickNumber == result.PickNumber),
            It.Is<IReadOnlyList<PickTaskLine>>(lines =>
                lines.Count == 1
                && lines[0].OrderAllocationId == allocId
                && lines[0].StockId == stockId
                && lines[0].ExpectedQuantity == 10m
                && lines[0].LineStatus == "Pending"),
            userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // SubmitAsync — state gating + request shape
    // ================================================================

    [Fact]
    public async Task Submit_NotFound_Throws()
    {
        var b = NewService();
        var req = new SubmitPickTaskRequest(Guid.NewGuid(), Array.Empty<PickedLineEntry>());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("Picked")]
    [InlineData("PartiallyPicked")]
    [InlineData("Cancelled")]
    public async Task Submit_TerminalState_Throws(string status)
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, status, Guid.NewGuid()),
                new List<PickTaskLine>()));

        var req = new SubmitPickTaskRequest(taskId, Array.Empty<PickedLineEntry>());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_EmptyRequest_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PickTaskLine>
                {
                    NewPickLine(Guid.NewGuid(), taskId, Guid.NewGuid(), Guid.NewGuid(), 5m),
                }));

        var req = new SubmitPickTaskRequest(taskId, Array.Empty<PickedLineEntry>());
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
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PickTaskLine>
                {
                    NewPickLine(line1, taskId, Guid.NewGuid(), Guid.NewGuid(), 5m),
                    NewPickLine(line2, taskId, Guid.NewGuid(), Guid.NewGuid(), 5m),
                }));

        // Only one of two lines in the request.
        var req = new SubmitPickTaskRequest(taskId, new[]
        {
            new PickedLineEntry(line1, 5m, "Picked", null, null),
        });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_PickedShortNoReason_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PickTaskLine>
                {
                    NewPickLine(lineId, taskId, Guid.NewGuid(), Guid.NewGuid(), 5m),
                }));

        var req = new SubmitPickTaskRequest(taskId, new[]
        {
            new PickedLineEntry(lineId, 3m, "Picked", ShortPickReason: null, Notes: null),
        });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_SkippedNoReason_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PickTaskLine>
                {
                    NewPickLine(lineId, taskId, Guid.NewGuid(), Guid.NewGuid(), 5m),
                }));

        var req = new SubmitPickTaskRequest(taskId, new[]
        {
            new PickedLineEntry(lineId, null, "Skipped", ShortPickReason: "  ", Notes: null),
        });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    [Fact]
    public async Task Submit_PickedQtyOverExpected_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Pending", Guid.NewGuid()),
                new List<PickTaskLine>
                {
                    NewPickLine(lineId, taskId, Guid.NewGuid(), Guid.NewGuid(), 5m),
                }));

        var req = new SubmitPickTaskRequest(taskId, new[]
        {
            new PickedLineEntry(lineId, 10m, "Picked", null, null),
        });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
    }

    // ================================================================
    // SubmitAsync — happy paths
    // ================================================================

    [Fact]
    public async Task Submit_FullPick_FlipsTaskPicked_FlipsSoPicked_DecrementsStock()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var soLineId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Pick task with one line, expected 5.
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Pending", soId),
                new List<PickTaskLine>
                {
                    NewPickLine(lineId, taskId, allocId, stockId, 5m),
                }));

        // Allocation lookup carries SalesOrderLineId.
        b.AllocRepo.Setup(r => r.GetActiveEntitiesBySalesOrderIdAsync(
                soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderAllocation>
            {
                NewAllocation(allocId, soLineId, stockId, qty: 5m),
            });

        // SetStartedAsync (Pending → InProgress) succeeds.
        b.PickRepo.Setup(r => r.SetStartedAsync(taskId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // SetCompletedAsync (InProgress → Picked) succeeds.
        b.PickRepo.Setup(r => r.SetCompletedAsync(taskId, "Picked", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // MarkPicked succeeds.
        b.AllocRepo.Setup(r => r.MarkPickedAsync(allocId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Post-commit SO read: PickedQuantity bumped to OrderedQuantity → all full.
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewSoHeader(soId, "Picking"),
                new List<SalesOrderLine>
                {
                    NewSoLine(soLineId, soId, ordered: 5m, allocated: 0m, picked: 5m),
                }));
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Picking", "Picked", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new SubmitPickTaskRequest(taskId, new[]
        {
            new PickedLineEntry(lineId, 5m, "Picked", null, null),
        });

        var result = await b.Service.SubmitAsync(TenantId, req, userId);

        Assert.Equal("Picked", result.TaskStatus);
        Assert.Equal("Picked", result.SalesOrderStatus);
        Assert.Equal(1, result.FullyPickedLineCount);
        Assert.Equal(0, result.ShortPickedLineCount);
        Assert.Equal(0, result.SkippedLineCount);
        Assert.Equal(5m, result.TotalPickedQuantity);

        // Stock OnHand decremented by picked qty (5).
        b.StockRepo.Verify(r => r.UpsertOnHandAsync(
            It.Is<StockKey>(k => k.LocationId == LocationId && k.ProductId == ProductId),
            -5m,
            It.Is<StockMovementContext>(c =>
                c.MovementType == StockMovementType.Pick
                && c.PerformedBy == userId
                && c.ReferenceType == "PickTaskLine"
                && c.ReferenceId == lineId),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Stock.QuantityAllocated decremented by full Expected (5).
        b.StockRepo.Verify(r => r.AdjustQuantityAllocatedAsync(
            stockId, -5m, userId, It.IsAny<CancellationToken>()), Times.Once);

        // SO line: PickedQuantity bumped, AllocatedQuantity decremented.
        b.SoRepo.Verify(r => r.AdjustLinePickedQuantityAsync(
            soLineId, 5m, userId, It.IsAny<CancellationToken>()), Times.Once);
        b.SoRepo.Verify(r => r.AdjustLineAllocatedQuantityAsync(
            soLineId, -5m, userId, It.IsAny<CancellationToken>()), Times.Once);

        // Allocation flipped Active → Picked.
        b.AllocRepo.Verify(r => r.MarkPickedAsync(
            allocId, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_ShortPick_FlipsTaskPartiallyPicked_FlipsSoPartiallyPicked()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var soLineId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Task line expected 5.
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "InProgress", soId)
                    .Tap(h => { h.StartedAt = DateTime.UtcNow; }),
                new List<PickTaskLine>
                {
                    NewPickLine(lineId, taskId, allocId, stockId, 5m),
                }));

        b.AllocRepo.Setup(r => r.GetActiveEntitiesBySalesOrderIdAsync(
                soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderAllocation>
            {
                NewAllocation(allocId, soLineId, stockId, qty: 5m),
            });

        // Already InProgress — SetStartedAsync NOT called.
        b.PickRepo.Setup(r => r.SetCompletedAsync(taskId, "PartiallyPicked", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.AllocRepo.Setup(r => r.MarkPickedAsync(allocId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Post-commit SO read: PickedQuantity=3 < OrderedQuantity=5.
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewSoHeader(soId, "Picking"),
                new List<SalesOrderLine>
                {
                    NewSoLine(soLineId, soId, ordered: 5m, picked: 3m),
                }));
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Picking", "PartiallyPicked", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new SubmitPickTaskRequest(taskId, new[]
        {
            new PickedLineEntry(lineId, 3m, "Picked", "Out of stock at bin", null),
        });

        var result = await b.Service.SubmitAsync(TenantId, req, userId);

        Assert.Equal("PartiallyPicked", result.TaskStatus);
        Assert.Equal("PartiallyPicked", result.SalesOrderStatus);
        Assert.Equal(0, result.FullyPickedLineCount);
        Assert.Equal(1, result.ShortPickedLineCount);
        Assert.Equal(0, result.SkippedLineCount);
        Assert.Equal(3m, result.TotalPickedQuantity);

        // No SetStartedAsync (already InProgress).
        b.PickRepo.Verify(r => r.SetStartedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_Skipped_NoOnHandWrite_ReleasesAllocation()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var soLineId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Pending", soId),
                new List<PickTaskLine>
                {
                    NewPickLine(lineId, taskId, allocId, stockId, 5m),
                }));

        b.AllocRepo.Setup(r => r.GetActiveEntitiesBySalesOrderIdAsync(
                soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderAllocation>
            {
                NewAllocation(allocId, soLineId, stockId, qty: 5m),
            });

        b.PickRepo.Setup(r => r.SetStartedAsync(taskId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.PickRepo.Setup(r => r.SetCompletedAsync(taskId, "PartiallyPicked", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.AllocRepo.Setup(r => r.MarkPickedAsync(allocId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.SoRepo.Setup(r => r.GetByIdAsync(soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesOrderDetail(
                NewSoHeader(soId, "Picking"),
                new List<SalesOrderLine>
                {
                    NewSoLine(soLineId, soId, ordered: 5m, picked: 0m),
                }));
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Picking", "PartiallyPicked", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var req = new SubmitPickTaskRequest(taskId, new[]
        {
            new PickedLineEntry(lineId, null, "Skipped", "Damaged stock", null),
        });

        var result = await b.Service.SubmitAsync(TenantId, req, userId);

        Assert.Equal("PartiallyPicked", result.TaskStatus);
        Assert.Equal(1, result.SkippedLineCount);
        Assert.Equal(0m, result.TotalPickedQuantity);

        // Skipped: NO Stock.OnHand write (qty=0 short-circuits the if).
        b.StockRepo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), It.IsAny<decimal>(),
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // BUT QuantityAllocated still decremented (release the reservation).
        b.StockRepo.Verify(r => r.AdjustQuantityAllocatedAsync(
            stockId, -5m, userId, It.IsAny<CancellationToken>()), Times.Once);

        // SO line: PickedQuantity NOT bumped (zero), AllocatedQuantity decremented.
        b.SoRepo.Verify(r => r.AdjustLinePickedQuantityAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        b.SoRepo.Verify(r => r.AdjustLineAllocatedQuantityAsync(
            soLineId, -5m, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_AllocationConcurrentlyFlipped_Throws()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var soLineId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Pending", soId),
                new List<PickTaskLine>
                {
                    NewPickLine(lineId, taskId, allocId, stockId, 5m),
                }));

        b.AllocRepo.Setup(r => r.GetActiveEntitiesBySalesOrderIdAsync(
                soId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderAllocation>
            {
                NewAllocation(allocId, soLineId, stockId, qty: 5m),
            });

        b.PickRepo.Setup(r => r.SetStartedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // Race: MarkPicked returns false (allocation already flipped).
        b.AllocRepo.Setup(r => r.MarkPickedAsync(
                allocId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var req = new SubmitPickTaskRequest(taskId, new[]
        {
            new PickedLineEntry(lineId, 5m, "Picked", null, null),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.SubmitAsync(TenantId, req, Guid.NewGuid()));
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
            () => b.Service.CancelAsync(TenantId, Guid.NewGuid(), "valid reason", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_Idempotent_ReturnsFalse()
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, "Cancelled", Guid.NewGuid()),
                new List<PickTaskLine>()));

        var changed = await b.Service.CancelAsync(TenantId, taskId, "any", Guid.NewGuid());
        Assert.False(changed);

        // No state-flip side effects.
        b.PickRepo.Verify(r => r.SetCancelledAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Picked")]
    [InlineData("PartiallyPicked")]
    public async Task Cancel_PostSubmitTerminal_Throws(string status)
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, status, Guid.NewGuid()),
                new List<PickTaskLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => b.Service.CancelAsync(TenantId, taskId, "any", Guid.NewGuid()));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("InProgress")]
    public async Task Cancel_Happy_FlipsTaskCancelled_FlipsSoAllocated(string fromStatus)
    {
        var b = NewService();
        var taskId = Guid.NewGuid();
        var soId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        b.PickRepo.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickTaskDetail(
                NewPickHeader(taskId, fromStatus, soId),
                new List<PickTaskLine>()));
        b.PickRepo.Setup(r => r.SetCancelledAsync(
                taskId, fromStatus, "needed elsewhere", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.SoRepo.Setup(r => r.SetStatusAsync(
                soId, "Picking", "Allocated", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var changed = await b.Service.CancelAsync(TenantId, taskId, "needed elsewhere", userId);

        Assert.True(changed);
        b.PickRepo.Verify(r => r.SetCancelledAsync(
            taskId, fromStatus, "needed elsewhere", userId, It.IsAny<CancellationToken>()),
            Times.Once);
        b.SoRepo.Verify(r => r.SetStatusAsync(
            soId, "Picking", "Allocated", userId, It.IsAny<CancellationToken>()),
            Times.Once);
        // No Stock or allocation writes (Cancel is light per design).
        b.StockRepo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), It.IsAny<decimal>(),
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        b.AllocRepo.Verify(r => r.MarkPickedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

// Tiny extension to build PickTask headers fluently in tests.
internal static class PickTaskTestExtensions
{
    public static PickTask Tap(this PickTask t, Action<PickTask> mut)
    {
        mut(t);
        return t;
    }
}
