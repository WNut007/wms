using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Counts;
using WMS.Common.Inventory;
using WMS.DAL.Repositories.Counts;
using WMS.DAL.Repositories.Inventory;
using WMS.Domain.Entities.Counts;
using WMS.Domain.Entities.Inventory;

namespace WMS.UnitTests.Services.Counts;

// Phase 12 — CycleCountService unit tests. Cover snapshot creation,
// per-line save validation, state transitions, separation-of-duties
// on Apply, and the per-line variance behaviour (write only when
// Counted + non-zero variance).
public class CycleCountServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid UomId = Guid.NewGuid();

    private static CycleCountService NewService(
        out Mock<ICycleCountRepository> repo,
        out Mock<IStockRepository> stockRepo)
    {
        repo = new Mock<ICycleCountRepository>();
        var factory = new Mock<ICycleCountRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        stockRepo = new Mock<IStockRepository>();
        var stockFactory = new Mock<IStockRepositoryFactory>();
        stockFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(stockRepo.Object);

        return new CycleCountService(
            factory.Object, stockFactory.Object,
            NullLogger<CycleCountService>.Instance);
    }

    private static Stock NewStockRow(decimal onHand) => new()
    {
        Id = Guid.NewGuid(),
        LocationId = LocationId,
        ProductId = ProductId,
        OwnerId = OwnerId,
        UomId = UomId,
        QuantityOnHand = onHand,
    };

    // ================================================================
    // CreateAsync — snapshot
    // ================================================================

    [Fact]
    public async Task Create_EmptySnapshot_Throws()
    {
        var sut = NewService(out _, out var stockRepo);
        stockRepo.Setup(r => r.GetPositiveOnHandByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Stock>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CreateAsync(TenantId,
                new CreateCycleCountRequest(WarehouseId, null, null), Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_BlankUserId_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreateAsync(TenantId,
                new CreateCycleCountRequest(WarehouseId, null, null), Guid.Empty));
    }

    [Fact]
    public async Task Create_HappyPath_AssignsNumber_PersistsLines()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var snapshot = new[] { NewStockRow(10m), NewStockRow(5m), NewStockRow(2m) };

        stockRepo.Setup(r => r.GetPositiveOnHandByWarehouseAsync(
                WarehouseId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        repo.Setup(r => r.CountForDatePrefixAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        CycleCount? capturedHeader = null;
        IReadOnlyList<CycleCountLine>? capturedLines = null;
        repo.Setup(r => r.CreateAsync(
                It.IsAny<CycleCount>(), It.IsAny<IReadOnlyList<CycleCountLine>>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<CycleCount, IReadOnlyList<CycleCountLine>, Guid, CancellationToken>(
                (h, ls, _, _) => { capturedHeader = h; capturedLines = ls; })
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new CycleCountDetail(
                    new CycleCount { Id = id, CountNumber = capturedHeader?.CountNumber ?? "" },
                    capturedLines ?? Array.Empty<CycleCountLine>()));

        var saved = await sut.CreateAsync(
            TenantId,
            new CreateCycleCountRequest(WarehouseId, null, "Q2 cycle"),
            Guid.NewGuid());

        Assert.NotNull(capturedHeader);
        Assert.Equal("Counting", capturedHeader!.Status);
        var prefix = $"CYC-{DateTime.UtcNow:yyyyMMdd}-";
        Assert.StartsWith(prefix, capturedHeader.CountNumber);
        Assert.EndsWith("0001", capturedHeader.CountNumber);

        Assert.NotNull(capturedLines);
        Assert.Equal(3, capturedLines!.Count);
        Assert.All(capturedLines, l => Assert.Null(l.CountedQuantity));
        Assert.All(capturedLines, l => Assert.Equal("Pending", l.LineStatus));
        // Line numbers 1..3.
        Assert.Equal(new[] { 1, 2, 3 }, capturedLines.Select(l => l.LineNumber).ToArray());
    }

    // ================================================================
    // SaveCountedQuantitiesAsync — validation
    // ================================================================

    [Fact]
    public async Task SaveCounts_NotInCountingState_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Review" },
                Array.Empty<CycleCountLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SaveCountedQuantitiesAsync(TenantId, id,
                Array.Empty<CountLineUpdate>(), Guid.NewGuid()));
    }

    [Fact]
    public async Task SaveCounts_InvalidLineStatus_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Counting" },
                Array.Empty<CycleCountLine>()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SaveCountedQuantitiesAsync(TenantId, id,
                new[] { new CountLineUpdate(Guid.NewGuid(), 5m, "Bogus", null) },
                Guid.NewGuid()));
    }

    [Fact]
    public async Task SaveCounts_CountedRequiresQuantity()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Counting" },
                Array.Empty<CycleCountLine>()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SaveCountedQuantitiesAsync(TenantId, id,
                new[] { new CountLineUpdate(Guid.NewGuid(), null, "Counted", null) },
                Guid.NewGuid()));
    }

    [Fact]
    public async Task SaveCounts_PendingForbidsQuantity()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Counting" },
                Array.Empty<CycleCountLine>()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SaveCountedQuantitiesAsync(TenantId, id,
                new[] { new CountLineUpdate(Guid.NewGuid(), 3m, "Pending", null) },
                Guid.NewGuid()));
    }

    [Fact]
    public async Task SaveCounts_HappyPath_CallsRepoBulkSave()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Counting" },
                Array.Empty<CycleCountLine>()));

        var updates = new[]
        {
            new CountLineUpdate(Guid.NewGuid(), 5m,   "Counted", null),
            new CountLineUpdate(Guid.NewGuid(), null, "Skipped", "missing"),
        };

        await sut.SaveCountedQuantitiesAsync(TenantId, id, updates, Guid.NewGuid());

        repo.Verify(r => r.SaveCountedQuantitiesAsync(
            id, It.Is<IReadOnlyList<(Guid, decimal?, string, string?)>>(l => l.Count == 2),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ================================================================
    // ApproveAndApplyAsync
    // ================================================================

    [Fact]
    public async Task Apply_AlreadyApplied_ReturnsFalse()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Applied" },
                Array.Empty<CycleCountLine>()));

        var changed = await sut.ApproveAndApplyAsync(TenantId, id, Guid.NewGuid());
        Assert.False(changed);
    }

    [Fact]
    public async Task Apply_NotInReview_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Counting" },
                Array.Empty<CycleCountLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApproveAndApplyAsync(TenantId, id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Apply_SelfApproval_Throws()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var counterId = Guid.NewGuid();
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Review", CountedBy = counterId },
                Array.Empty<CycleCountLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApproveAndApplyAsync(TenantId, id, counterId));

        stockRepo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), It.IsAny<decimal>(),
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Apply_HappyPath_WritesOnlyVarianceLines_FlipsStatus()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var approverId = Guid.NewGuid();
        var counterId = Guid.NewGuid();
        var id = Guid.NewGuid();

        // 4 lines:
        //  [0] Counted, variance +2  → write
        //  [1] Counted, variance 0   → skip (verified-as-correct)
        //  [2] Counted, variance -3  → write
        //  [3] Pending (not counted) → skip
        var lines = new List<CycleCountLine>
        {
            new()
            {
                Id = Guid.NewGuid(), LineStatus = "Counted",
                LocationId = LocationId, ProductId = ProductId,
                OwnerId = OwnerId, UomId = UomId,
                ExpectedQuantity = 5m, CountedQuantity = 7m,
            },
            new()
            {
                Id = Guid.NewGuid(), LineStatus = "Counted",
                LocationId = LocationId, ProductId = ProductId,
                OwnerId = OwnerId, UomId = UomId,
                ExpectedQuantity = 3m, CountedQuantity = 3m,
            },
            new()
            {
                Id = Guid.NewGuid(), LineStatus = "Counted",
                LocationId = LocationId, ProductId = ProductId,
                OwnerId = OwnerId, UomId = UomId,
                ExpectedQuantity = 10m, CountedQuantity = 7m,
            },
            new()
            {
                Id = Guid.NewGuid(), LineStatus = "Pending",
                LocationId = LocationId, ProductId = ProductId,
                OwnerId = OwnerId, UomId = UomId,
                ExpectedQuantity = 4m, CountedQuantity = null,
            },
        };

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount
                {
                    Id = id, CountNumber = "CYC-X",
                    Status = "Review", CountedBy = counterId,
                },
                lines));

        var captured = new List<(decimal Delta, StockMovementContext Ctx)>();
        stockRepo.Setup(r => r.UpsertOnHandAsync(
                It.IsAny<StockKey>(), It.IsAny<decimal>(),
                It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()))
            .Callback<StockKey, decimal, StockMovementContext, CancellationToken>(
                (_, d, c, _) => captured.Add((d, c)))
            .ReturnsAsync(new Stock { Id = Guid.NewGuid(), QuantityOnHand = 0 });

        repo.Setup(r => r.SetAppliedAsync(id, approverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.ApproveAndApplyAsync(TenantId, id, approverId);

        Assert.True(ok);
        Assert.Equal(2, captured.Count);  // only the 2 non-zero-variance Counted lines
        Assert.Contains(captured, x => x.Delta == 2m);
        Assert.Contains(captured, x => x.Delta == -3m);
        Assert.All(captured, x =>
        {
            Assert.Equal(StockMovementType.Cycle, x.Ctx.MovementType);
            Assert.Equal("CycleCountLine", x.Ctx.ReferenceType);
        });

        repo.Verify(r => r.SetAppliedAsync(id, approverId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Apply_AllZeroVariance_StillFlipsStatus_NoStockWrites()
    {
        var sut = NewService(out var repo, out var stockRepo);
        var approverId = Guid.NewGuid();
        var counterId = Guid.NewGuid();
        var id = Guid.NewGuid();

        var lines = new List<CycleCountLine>
        {
            new()
            {
                Id = Guid.NewGuid(), LineStatus = "Counted",
                LocationId = LocationId, ProductId = ProductId,
                OwnerId = OwnerId, UomId = UomId,
                ExpectedQuantity = 5m, CountedQuantity = 5m,
            },
        };

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Review", CountedBy = counterId },
                lines));
        repo.Setup(r => r.SetAppliedAsync(id, approverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await sut.ApproveAndApplyAsync(TenantId, id, approverId);
        Assert.True(ok);
        stockRepo.Verify(r => r.UpsertOnHandAsync(
            It.IsAny<StockKey>(), It.IsAny<decimal>(),
            It.IsAny<StockMovementContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ================================================================
    // CancelAsync
    // ================================================================

    [Fact]
    public async Task Cancel_BlankReason_Throws()
    {
        var sut = NewService(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CancelAsync(TenantId, Guid.NewGuid(), "  ", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_AlreadyApplied_Throws()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Applied" },
                Array.Empty<CycleCountLine>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CancelAsync(TenantId, id, "too late", Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsFalse()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Cancelled" },
                Array.Empty<CycleCountLine>()));

        var changed = await sut.CancelAsync(TenantId, id, "duplicate", Guid.NewGuid());
        Assert.False(changed);
    }

    [Fact]
    public async Task Cancel_HappyPath_TriesBothFromStates()
    {
        var sut = NewService(out var repo, out _);
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountDetail(
                new CycleCount { Id = id, Status = "Review" },
                Array.Empty<CycleCountLine>()));

        // First call (Counting source) returns false; second call (Review) returns true.
        repo.Setup(r => r.SetCancelledAsync(id, "Counting", "abandon", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.SetCancelledAsync(id, "Review",   "abandon", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var changed = await sut.CancelAsync(TenantId, id, "abandon", userId);
        Assert.True(changed);
    }
}
