using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Inventory;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.UnitTests.Services.Inventory;

// Phase 11A (ADR-013) — AdjustmentService unit tests. Cover the
// validation, separation-of-duties, and idempotency paths plus the
// happy approve flow's wiring to Stock + repo SetApplied.
public class AdjustmentServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid UomId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    private static AdjustmentService NewService(
        out Mock<IAdjustmentRepository> adjRepo,
        out Mock<IStockRepository> stockRepo)
    {
        adjRepo = new Mock<IAdjustmentRepository>();
        var adjFactory = new Mock<IAdjustmentRepositoryFactory>();
        adjFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(adjRepo.Object);

        stockRepo = new Mock<IStockRepository>();
        var stockFactory = new Mock<IStockRepositoryFactory>();
        stockFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(stockRepo.Object);

        return new AdjustmentService(
            adjFactory.Object,
            stockFactory.Object,
            NullLogger<AdjustmentService>.Instance);
    }

    private static CreateAdjustmentRequest NewRequest(
        decimal delta = 5m, string reason = "Found") =>
        new(LocationId, ProductId, null, null, OwnerId, UomId, WarehouseId,
            delta, reason, null);

    // ================================================================
    // CreateAsync — validation
    // ================================================================

    [Fact]
    public async Task Create_ZeroDelta_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(delta: 0m), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_BlankReason_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(reason: ""), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_UnknownReason_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(reason: "NotARealReason"), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_EmptyUserId_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId, NewRequest(), Guid.Empty));
    }

    [Fact]
    public async Task Create_HappyPath_AssignsNumberAndPersists()
    {
        var sut = NewService(out var adjRepo, out _);
        var requesterId = Guid.NewGuid();

        adjRepo.Setup(r => r.CountForDatePrefixAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        Adjustment? captured = null;
        adjRepo.Setup(r => r.CreateAsync(
                It.IsAny<Adjustment>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Adjustment, Guid, CancellationToken>((a, _, _) => captured = a)
            .Returns(Task.CompletedTask);

        adjRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new Adjustment { Id = id, AdjustmentNumber = captured?.AdjustmentNumber ?? "" });

        var saved = await sut.CreateAsync(TenantId, NewRequest(delta: -3m), requesterId);

        Assert.NotNull(captured);
        Assert.Equal(-3m, captured!.QuantityDelta);
        Assert.Equal("Found", captured.Reason);
        Assert.Equal("Pending", captured.Status);
        Assert.Null(captured.StockId);  // resolved on Apply, not Create
        // Number format: ADJ-YYYYMMDD-0008 (existing 7 + 1).
        var prefix = $"ADJ-{DateTime.UtcNow:yyyyMMdd}-";
        Assert.StartsWith(prefix, captured.AdjustmentNumber);
        Assert.EndsWith("0008", captured.AdjustmentNumber);
    }

    // ================================================================
    // ApproveAsync
    // ================================================================

    [Fact]
    public async Task Approve_AlreadyApplied_ReturnsFalse_NoStockWrite()
    {
        var sut = NewService(out var adjRepo, out var stockRepo);
        var id = Guid.NewGuid();

        adjRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adjustment { Id = id, Status = "Applied" });

        var changed = await sut.ApproveAsync(TenantId, id, Guid.NewGuid());

        Assert.False(changed);
        stockRepo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), It.IsAny<decimal>(),
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Approve_AlreadyRejected_Throws()
    {
        var sut = NewService(out var adjRepo, out _);
        var id = Guid.NewGuid();
        adjRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adjustment { Id = id, Status = "Rejected" });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApproveAsync(TenantId, id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Approve_SelfApproval_Throws()
    {
        var sut = NewService(out var adjRepo, out var stockRepo);
        var requesterId = Guid.NewGuid();
        var id = Guid.NewGuid();

        adjRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adjustment
            {
                Id = id, Status = "Pending", RequestedBy = requesterId,
                LocationId = LocationId, ProductId = ProductId,
                OwnerId = OwnerId, UomId = UomId, WarehouseId = WarehouseId,
                QuantityDelta = 5m, Reason = "Found",
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApproveAsync(TenantId, id, requesterId));

        stockRepo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), It.IsAny<decimal>(),
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Approve_HappyPath_WritesStock_FlipsStatus()
    {
        var sut = NewService(out var adjRepo, out var stockRepo);
        var requesterId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        adjRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adjustment
            {
                Id = id, AdjustmentNumber = "ADJ-X",
                Status = "Pending", RequestedBy = requesterId,
                LocationId = LocationId, ProductId = ProductId,
                OwnerId = OwnerId, UomId = UomId, WarehouseId = WarehouseId,
                QuantityDelta = -7m, Reason = "Damaged",
            });

        decimal capturedDelta = 0;
        StockMovementContext? capturedCtx = null;
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, StockMovementContext, CancellationToken>(
                (_, d, c, _) => { capturedDelta = d; capturedCtx = c; })
            .ReturnsAsync(new Stock { Id = stockId, QuantityOnHand = 0 });

        adjRepo.Setup(r => r.SetAppliedAsync(
                id, stockId, approverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.ApproveAsync(TenantId, id, approverId);

        Assert.True(ok);
        Assert.Equal(-7m, capturedDelta);
        Assert.NotNull(capturedCtx);
        Assert.Equal(StockMovementType.Adjust, capturedCtx!.MovementType);
        Assert.Equal("Adjustment", capturedCtx.ReferenceType);
        Assert.Equal(id, capturedCtx.ReferenceId);
        adjRepo.Verify(r => r.SetAppliedAsync(
            id, stockId, approverId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // RejectAsync
    // ================================================================

    [Fact]
    public async Task Reject_BlankReason_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RejectAsync(TenantId, Guid.NewGuid(), "  ", Guid.NewGuid()));
    }

    [Fact]
    public async Task Reject_AlreadyRejected_ReturnsFalse()
    {
        var sut = NewService(out var adjRepo, out _);
        var id = Guid.NewGuid();
        adjRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adjustment { Id = id, Status = "Rejected" });

        var changed = await sut.RejectAsync(TenantId, id, "duplicate", Guid.NewGuid());
        Assert.False(changed);
    }

    [Fact]
    public async Task Reject_SelfReject_Throws()
    {
        var sut = NewService(out var adjRepo, out _);
        var requesterId = Guid.NewGuid();
        var id = Guid.NewGuid();
        adjRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adjustment { Id = id, Status = "Pending", RequestedBy = requesterId });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RejectAsync(TenantId, id, "self-reject attempt", requesterId));
    }

    [Fact]
    public async Task Reject_HappyPath_FlipsStatus_CallsRepoWithTrimmedReason()
    {
        var sut = NewService(out var adjRepo, out _);
        var id = Guid.NewGuid();
        var rejecterId = Guid.NewGuid();

        adjRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adjustment { Id = id, Status = "Pending", RequestedBy = Guid.NewGuid() });
        adjRepo.Setup(r => r.SetRejectedAsync(
                id, "delta wrong", rejecterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.RejectAsync(TenantId, id, "  delta wrong  ", rejecterId);

        Assert.True(ok);
        adjRepo.Verify(r => r.SetRejectedAsync(
            id, "delta wrong", rejecterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
