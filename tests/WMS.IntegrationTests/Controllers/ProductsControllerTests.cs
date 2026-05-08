using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Master;
using WMS.Web.Controllers;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

// Pure-controller tests via Moq — no HTTP pipeline, no DB. Same
// placement reasoning as the mappers: WMS.Web is net8.0-windows so
// these can't live in WMS.UnitTests.
public class ProductsControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static (ProductsController Controller, Mock<IProductRepository> Repo)
        Build(IDocumentStorageService? docs = null)
    {
        var repo    = new Mock<IProductRepository>();
        var factory = new Mock<IProductRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var docsImpl = docs ?? Mock.Of<IDocumentStorageService>(d =>
            d.ListByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<DocumentMetadata>()));

        return (new ProductsController(factory.Object, tenant.Object, docsImpl), repo);
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
        var (ctrl, repo) = Build();
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
        var (ctrl, repo) = Build();
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
        var (ctrl, repo) = Build();
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
        var (ctrl, repo) = Build();
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
        var (ctrl, repo) = Build();
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
        var (ctrl, repo) = Build();
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
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("PROD-9999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductListRow?)null);

        var result = await ctrl.Detail("PROD-9999", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_KnownSku_ReturnsViewWithRealStockTile()
    {
        var (ctrl, repo) = Build();
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
    public async Task Detail_KnownSku_ActivitiesStillHardcoded_TD010Regression()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("PROD-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("PROD-0001"));

        var result = await ctrl.Detail("PROD-0001", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        // TD-010: Activity tab is wired in Phase 6C. Until then, exactly
        // 5 hardcoded entries are emitted. If this count changes,
        // the wiring may have happened — update or close the TD.
        Assert.Equal(5, vm.Activities.Count);
        Assert.Contains(vm.Activities, a => a.Title.Contains("uploaded 3 product images"));
    }
}
