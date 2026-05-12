using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.Web.Controllers;
using WMS.Web.Models.Master;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

// Pure-controller tests via Moq — no HTTP pipeline, no DB. Same
// placement reasoning as the mappers: WMS.Web is net8.0-windows so
// these can't live in WMS.UnitTests.
public class ProductsControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static (ProductsController Controller,
                    Mock<IProductRepository> Repo,
                    Mock<IStockMovementRepository> Movements)
        Build(IDocumentStorageService? docs = null, Guid? userWarehouseId = null)
    {
        var repo    = new Mock<IProductRepository>();
        var factory = new Mock<IProductRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        var movements = new Mock<IStockMovementRepository>();
        var movementFactory = new Mock<IStockMovementRepositoryFactory>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        movementFactory.Setup(x => x.For(It.IsAny<Guid>())).Returns(movements.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        // Default: empty movement list — Detail tests that don't care
        // about activity see the empty-state path.
        movements.Setup(m => m.GetByProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockMovementListRow>());

        var docsImpl = docs ?? Mock.Of<IDocumentStorageService>(d =>
            d.ListByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<DocumentMetadata>()));

        // Phase 7 admin write-side dependencies. Read-side tests
        // (Index / GetData / Detail) don't exercise these; default
        // empty-list lookup factories + a no-op Validator suffice.
        // Phase 7F tests introduce richer setup as needed.
        var categoryRepo = new Mock<IProductCategoryRepository>();
        categoryRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LookupItem>());
        var categoryFactory = new Mock<IProductCategoryRepositoryFactory>();
        categoryFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(categoryRepo.Object);

        var uomRepo = new Mock<IUomRepository>();
        uomRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LookupItem>());
        var uomFactory = new Mock<IUomRepositoryFactory>();
        uomFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(uomRepo.Object);

        var createValidator = new Mock<IValidator<ProductCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ProductCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult());

        var editValidator = new Mock<IValidator<ProductEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ProductEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult());

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        // Default: no warehouse context — SearchAsync no-context tests
        // rely on this. Tests exercising the warehouse fallback pass a
        // non-null userWarehouseId.
        if (userWarehouseId is not null)
            currentUser.SetupGet(u => u.WarehouseId).Returns(userWarehouseId);

        return (new ProductsController(
                    factory.Object,
                    movementFactory.Object,
                    tenant.Object,
                    docsImpl,
                    categoryFactory.Object,
                    uomFactory.Object,
                    createValidator.Object,
                    editValidator.Object,
                    currentUser.Object),
                repo, movements);
    }

    private static ProductListRow Row(string code, string status = "Active") =>
        new(
            Id:           Guid.NewGuid(),
            Code:         code,
            Name:         $"Product {code}",
            Status:       status,
            Brand:        "Apple",
            CategoryId:   Guid.NewGuid(),
            CategoryCode: "DEMO",
            StockOnHand:  100m,
            CreatedAt:    DateTime.UtcNow.AddDays(-30),
            UpdatedAt:    DateTime.UtcNow.AddHours(-2));

    [Fact]
    public async Task GetData_DefaultParams_PassesPageOneAndDefaults()
    {
        var (ctrl, repo, _) = Build();
        ProductFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ProductFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ProductListRow> { Items = new(), Total = 0, Page = 1, PageSize = 20 });

        await ctrl.GetData();

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Page);
        Assert.Equal(20, captured.PageSize);
        Assert.Null(captured.Status);
        Assert.Null(captured.CategoryCode);
        Assert.Equal("name", captured.SortBy);
        Assert.False(captured.SortDesc);
    }

    [Theory]
    [InlineData("active",       "Active")]
    [InlineData("inactive",     "Inactive")]
    [InlineData("discontinued", "Discontinued")]
    [InlineData("draft",        "Draft")]
    public async Task GetData_StatusFilter_MappedToPascalCase(string wire, string expected)
    {
        var (ctrl, repo, _) = Build();
        ProductFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ProductFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ProductListRow> { Items = new() });

        await ctrl.GetData(status: wire);

        Assert.Equal(expected, captured!.Status);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("out_of_stock")]    // mock-only, not a real schema state
    [InlineData(null)]
    public async Task GetData_UnknownStatus_DropsFilter(string? wire)
    {
        var (ctrl, repo, _) = Build();
        ProductFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ProductFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ProductListRow> { Items = new() });

        await ctrl.GetData(status: wire);

        Assert.Null(captured!.Status);
    }

    [Theory]
    [InlineData("all", null)]
    [InlineData(null, null)]
    [InlineData("",   null)]
    [InlineData("DEMO", "DEMO")]
    public async Task GetData_CategoryFilter_AllOrEmpty_DropsFilter(string? wire, string? expected)
    {
        var (ctrl, repo, _) = Build();
        ProductFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ProductFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ProductListRow> { Items = new() });

        await ctrl.GetData(category: wire);

        Assert.Equal(expected, captured!.CategoryCode);
    }

    [Fact]
    public async Task GetData_JsonShape_HasExpectedKeys()
    {
        var (ctrl, repo, _) = Build();
        repo.Setup(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProductListRow>
            {
                Items = new() { Row("PROD-0001") },
                Total = 1,
                Page = 1,
                PageSize = 20,
                TotalPages = 1,
            });

        var result = (JsonResult)await ctrl.GetData();

        // Project to a JSON-ish bag and prod every key the JS list view
        // reads (Index.cshtml lines 184-198).
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        foreach (var key in new[] { "sku", "name", "brand", "category",
                                    "iconClass", "iconColor", "price",
                                    "stockOnHand", "status",
                                    "updatedAt", "updatedAtRelative" })
        {
            Assert.Contains($"\"{key}\":", json);
        }
        Assert.Contains("\"total\":1", json);
    }

    [Fact]
    public async Task GetData_StatusInResponse_IsLowercaseWire()
    {
        var (ctrl, repo, _) = Build();
        repo.Setup(r => r.GetPagedAsync(It.IsAny<ProductFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProductListRow>
            {
                Items = new() { Row("PROD-0020", status: "Discontinued") },
            });

        var result = (JsonResult)await ctrl.GetData();
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);

        // ToWire collapses to lowercase before serialisation.
        Assert.Contains("\"status\":\"discontinued\"", json);
        Assert.DoesNotContain("\"status\":\"Discontinued\"", json);
    }

    [Fact]
    public async Task Detail_UnknownSku_ReturnsNotFound()
    {
        var (ctrl, repo, _) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("PROD-9999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductListRow?)null);

        var result = await ctrl.Detail("PROD-9999", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_KnownSku_ReturnsViewWithRealStockTile()
    {
        var (ctrl, repo, _) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("PROD-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("PROD-0001"));

        var result = await ctrl.Detail("PROD-0001", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Equal("PROD-0001", vm.EntityId);
        // Stock tile is real — comes from the repo's StockOnHand column.
        var stockTile = vm.Stats.Single(s => s.Label == "Stock");
        Assert.Equal("100", stockTile.Value);
        // Reserved / Sold YTD remain stubs (no orders schema yet).
        Assert.Equal("—", vm.Stats.Single(s => s.Label == "Reserved").Value);
        Assert.Equal("—", vm.Stats.Single(s => s.Label == "Sold YTD").Value);
        // Price tile NOT present — pricing is owner-scoped (TD-012).
        Assert.DoesNotContain(vm.Stats, s => s.Label == "Price");
    }

    [Fact]
    public async Task Detail_NoMovements_ActivitiesEmpty_TD010Closed()
    {
        // Phase 6C closes TD-010. The hardcoded 5-entry mock list is
        // gone — Detail now reads real movements via
        // IStockMovementRepository.GetByProductAsync. New products
        // (and DEMO-001 — forward-only per ADR-014) have no history,
        // so the empty-state path is the expected default.
        // _ActivityPanel renders "No activity yet." in this case.
        var (ctrl, repo, _) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("PROD-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("PROD-0001"));

        var result = await ctrl.Detail("PROD-0001", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Empty(vm.Activities);
    }

    [Fact]
    public async Task Detail_WithMovements_ActivitiesMappedFromRepo()
    {
        var (ctrl, repo, movements) = Build();
        var productId = Guid.NewGuid();

        repo.Setup(r => r.GetListRowByCodeAsync("PROD-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("PROD-0001") with { Id = productId });

        movements.Setup(m => m.GetByProductAsync(
                productId,
                It.IsAny<DateTime?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StockMovementListRow(
                    Id:               Guid.NewGuid(),
                    MovementType:     StockMovementType.Receive,
                    QuantityDelta:    50m,
                    FromLocationCode: null,
                    ToLocationCode:   "WH-MAIN",
                    ReferenceType:    "ReceivingLine",
                    Notes:            null,
                    PerformedByName:  "Maya Rodriguez",
                    PerformedAt:      DateTime.UtcNow.AddHours(-2)),
                new StockMovementListRow(
                    Id:               Guid.NewGuid(),
                    MovementType:     StockMovementType.Putaway,
                    QuantityDelta:    -50m,
                    FromLocationCode: "STAGE-01",
                    ToLocationCode:   "BIN-A1",
                    ReferenceType:    "Putaway",
                    Notes:            null,
                    PerformedByName:  "Maya Rodriguez",
                    PerformedAt:      DateTime.UtcNow.AddHours(-1)),
            });

        var result = await ctrl.Detail("PROD-0001", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        // Order is whatever the repo returns — controller doesn't
        // re-sort. The mapper is exercised by both rows.
        Assert.Equal(2, vm.Activities.Count);
        Assert.Contains(vm.Activities,
            a => a.Title.Contains("received 50 units") && a.Title.Contains(" at WH-MAIN"));
        Assert.Contains(vm.Activities,
            a => a.Title.Contains("moved 50 units") && a.Title.Contains(" from STAGE-01"));
    }

    // ============================================================
    // Block 4.5.1 chunk d — SearchAsync (Tom Select callback)
    // ============================================================

    [Fact]
    public async Task SearchAsync_NoWarehouseContext_ReturnsBadRequest()
    {
        // Neither query param nor user claim has a warehouse — operator
        // sees an explicit error instead of silently empty results.
        var (ctrl, repo, _) = Build();

        var result = await ctrl.SearchAsync();

        Assert.IsType<BadRequestObjectResult>(result);
        repo.Verify(
            r => r.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_QueryWarehouseId_OverridesUserFallback()
    {
        // Explicit warehouseId in the query string wins over the
        // user's default warehouse — supports cross-warehouse search
        // from the desktop PO grid (operator on warehouse A creating
        // a PO destined for warehouse B).
        var userWh    = Guid.NewGuid();
        var explicitWh = Guid.NewGuid();
        var (ctrl, repo, _) = Build(userWarehouseId: userWh);

        Guid? capturedWh = null;
        repo.Setup(r => r.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, int, Guid, CancellationToken>((_, _, w, _) => capturedWh = w)
            .ReturnsAsync(Array.Empty<ProductSearchResult>());

        await ctrl.SearchAsync(warehouseId: explicitWh);

        Assert.Equal(explicitWh, capturedWh);
    }

    [Fact]
    public async Task SearchAsync_NoQueryWarehouseId_FallsBackToUserClaim()
    {
        var userWh = Guid.NewGuid();
        var (ctrl, repo, _) = Build(userWarehouseId: userWh);

        Guid? capturedWh = null;
        repo.Setup(r => r.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, int, Guid, CancellationToken>((_, _, w, _) => capturedWh = w)
            .ReturnsAsync(Array.Empty<ProductSearchResult>());

        await ctrl.SearchAsync();

        Assert.Equal(userWh, capturedWh);
    }

    [Fact]
    public async Task SearchAsync_DefaultTake_Is50()
    {
        var (ctrl, repo, _) = Build(userWarehouseId: Guid.NewGuid());
        int? capturedTake = null;
        repo.Setup(r => r.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, int, Guid, CancellationToken>((_, t, _, _) => capturedTake = t)
            .ReturnsAsync(Array.Empty<ProductSearchResult>());

        await ctrl.SearchAsync();

        Assert.Equal(50, capturedTake);
    }

    [Theory]
    [InlineData(0,    1)]     // floor — defensive against ?take=0
    [InlineData(-5,   1)]     // floor — defensive against negative
    [InlineData(50,   50)]    // pass-through within bounds
    [InlineData(100,  100)]   // ceiling — exact
    [InlineData(500,  100)]   // ceiling — clamp from above
    [InlineData(9999, 100)]   // ceiling — DoS attempt
    public async Task SearchAsync_Take_ClampedToOneToOneHundred(int input, int expected)
    {
        var (ctrl, repo, _) = Build(userWarehouseId: Guid.NewGuid());
        int? capturedTake = null;
        repo.Setup(r => r.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, int, Guid, CancellationToken>((_, t, _, _) => capturedTake = t)
            .ReturnsAsync(Array.Empty<ProductSearchResult>());

        await ctrl.SearchAsync(take: input);

        Assert.Equal(expected, capturedTake);
    }

    [Fact]
    public async Task SearchAsync_Query_PassedThroughToRepo()
    {
        var (ctrl, repo, _) = Build(userWarehouseId: Guid.NewGuid());
        string? capturedQ = null;
        repo.Setup(r => r.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, int, Guid, CancellationToken>((q, _, _, _) => capturedQ = q)
            .ReturnsAsync(Array.Empty<ProductSearchResult>());

        await ctrl.SearchAsync(q: "iphone");

        Assert.Equal("iphone", capturedQ);
    }

    [Fact]
    public async Task SearchAsync_NullQuery_PassedAsNullToRepo()
    {
        // Pin the contract: empty query stays null (don't coerce to
        // empty string). Repo's SQL guard reads `@QLike IS NULL` and
        // returns the first `take` rows in code order.
        var (ctrl, repo, _) = Build(userWarehouseId: Guid.NewGuid());
        string? capturedQ = "not-null-sentinel";
        repo.Setup(r => r.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, int, Guid, CancellationToken>((q, _, _, _) => capturedQ = q)
            .ReturnsAsync(Array.Empty<ProductSearchResult>());

        await ctrl.SearchAsync();

        Assert.Null(capturedQ);
    }

    [Fact]
    public async Task SearchAsync_JsonShape_HasExpectedKeys()
    {
        var (ctrl, repo, _) = Build(userWarehouseId: Guid.NewGuid());
        repo.Setup(r => r.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ProductSearchResult(
                    Id:     Guid.NewGuid(),
                    Code:   "PROD-0001",
                    Name:   "iPhone 15",
                    Brand:  "Apple",
                    OnHand: 42m),
            });

        var result = (JsonResult)await ctrl.SearchAsync(q: "iphone");

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        // Tom Select's render template reads exactly these keys.
        foreach (var key in new[] { "id", "code", "name", "brand", "onHand" })
            Assert.Contains($"\"{key}\":", json);
        // Flat array shape — NOT wrapped in { items: [...] }. Tom
        // Select's load() callback signature expects `callback(arr)`.
        Assert.StartsWith("[", json);
    }

    [Fact]
    public async Task Detail_FetchesMovements_WithProductIdFromListRow()
    {
        // Pin the contract: the controller looks up movements by the
        // resolved product Id (from GetListRowByCodeAsync), NOT by SKU.
        // Catches accidental SKU-string passing — would compile but
        // miss every Stock row at runtime.
        var (ctrl, repo, movements) = Build();
        var productId = Guid.NewGuid();
        Guid? capturedId = null;

        repo.Setup(r => r.GetListRowByCodeAsync("PROD-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("PROD-0001") with { Id = productId });

        movements.Setup(m => m.GetByProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime?, int, CancellationToken>((id, _, _, _) => capturedId = id)
            .ReturnsAsync(Array.Empty<StockMovementListRow>());

        await ctrl.Detail("PROD-0001", CancellationToken.None);

        Assert.Equal(productId, capturedId);
    }
}
