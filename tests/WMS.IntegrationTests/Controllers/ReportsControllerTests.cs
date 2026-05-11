using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Reports;
using WMS.Web.Controllers;
using WMS.Web.ViewModels.Reports;

namespace WMS.IntegrationTests.Controllers;

// Phase 23 — ReportsController surface tests. Repo factory is mocked
// so the controller's view-action + export-action plumbing is covered
// without hitting SQL. Real aggregation SQL is exercised manually via
// browser smoke (acceptable since the queries are read-only and
// idempotent — same TD-006 family as other reports/SQL gaps).
public class ReportsControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record Build(ReportsController Controller, Mock<IReportRepository> Repo);

    private static Build BuildController()
    {
        var repo = new Mock<IReportRepository>();
        var factory = new Mock<IReportRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        // Default-stub every method so tests that only care about one
        // shape don't have to wire all 13.
        repo.Setup(r => r.GetInventorySummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventorySummary(0, 0, 0, 0));
        repo.Setup(r => r.GetStockByWarehouseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockByWarehouseRow>());
        repo.Setup(r => r.GetStockAgingBucketsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockAgingBucket>());
        repo.Setup(r => r.GetTopProductsByQuantityAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TopProductRow>());
        repo.Setup(r => r.GetSlowMoversAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SlowMoverRow>());
        repo.Setup(r => r.GetOrdersByStatusAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OrderStatusCount>());
        repo.Setup(r => r.GetOrdersByDateAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OrdersByDateRow>());
        repo.Setup(r => r.GetTopCustomersAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TopCustomerRow>());
        repo.Setup(r => r.GetFulfillmentCycleAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FulfillmentCycleRow>());
        repo.Setup(r => r.GetPicksByDayAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MovementByDayRow>());
        repo.Setup(r => r.GetPacksByDayAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MovementByDayRow>());
        repo.Setup(r => r.GetCycleCountVarianceAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CycleCountVarianceSummary(0, 0, 0, 0));
        repo.Setup(r => r.GetOnTimeShippingAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnTimeShippingSummary(0, 0));
        repo.Setup(r => r.GetTopPickersAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TopOperatorRow>());

        return new Build(new ReportsController(factory.Object, tenant.Object), repo);
    }

    [Fact]
    public void Index_ReturnsView()
    {
        var (ctrl, _) = BuildController();
        var result = ctrl.Index();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Inventory_ReturnsViewWithModel()
    {
        var (ctrl, repo) = BuildController();
        repo.Setup(r => r.GetInventorySummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventorySummary(1000, 100, 42, 17));

        var result = await ctrl.Inventory();

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<InventoryReportViewModel>(view.Model);
        Assert.Equal(1000m, vm.Summary.TotalOnHand);
        Assert.Equal(42, vm.Summary.DistinctProducts);
    }

    [Fact]
    public async Task Orders_DefaultsToMonthPreset_WhenRangeOmitted()
    {
        var (ctrl, _) = BuildController();
        var result = await ctrl.Orders(range: null);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<OrderAnalyticsViewModel>(view.Model);
        Assert.Equal("month", vm.Preset);
    }

    [Theory]
    [InlineData("today", "today")]
    [InlineData("week", "week")]
    [InlineData("month", "month")]
    [InlineData("quarter", "quarter")]
    [InlineData("year", "year")]
    [InlineData("nonsense", "month")]   // unknown falls to default
    [InlineData(null,       "month")]
    public async Task Orders_RangeFlowsToViewModel(string? input, string expected)
    {
        var (ctrl, _) = BuildController();
        var result = await ctrl.Orders(range: input);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<OrderAnalyticsViewModel>(view.Model);
        Assert.Equal(expected, vm.Preset);
    }

    [Fact]
    public async Task Kpis_ReturnsViewWithModel()
    {
        var (ctrl, repo) = BuildController();
        repo.Setup(r => r.GetOnTimeShippingAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnTimeShippingSummary(100, 95));

        var result = await ctrl.Kpis();

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<KpiReportViewModel>(view.Model);
        Assert.Equal(95m, vm.OnTimePercentage);
    }

    [Fact]
    public async Task ExportInventory_ReturnsXlsxFile()
    {
        var (ctrl, _) = BuildController();
        var result = await ctrl.ExportInventory();
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.ContentType);
        Assert.StartsWith("inventory-report-", file.FileDownloadName);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
        Assert.NotEmpty(file.FileContents);
    }

    [Fact]
    public async Task ExportOrders_FileNameIncludesRange()
    {
        var (ctrl, _) = BuildController();
        var result = await ctrl.ExportOrders(range: "week");
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Contains("orders-week-", file.FileDownloadName);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
    }

    [Fact]
    public async Task ExportKpis_FileNameIncludesRange()
    {
        var (ctrl, _) = BuildController();
        var result = await ctrl.ExportKpis(range: "year");
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Contains("kpis-year-", file.FileDownloadName);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
    }
}
