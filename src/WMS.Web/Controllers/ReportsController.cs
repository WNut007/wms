using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Reports;
using WMS.Web.Filters;
using WMS.Web.ViewModels.Reports;

namespace WMS.Web.Controllers;

// Phase 23 — first v3.0.0 chapter phase. Reports module top-level
// sidebar entry; landing page with 3 sub-report cards (Inventory /
// Orders / KPIs).
//
// Permission: single REPORTS.VIEW (seeded by Migration_20260511_033;
// granted to MANAGER by 034; ADMIN gets it via BLL bypass). Per-report
// permission split is a TD.
//
// Aggregation surface: IReportRepository (T2 wired; T3/T4 add the
// remaining methods). Excel export endpoints land in T5.
[Authorize]
[RequirePermission("REPORTS.VIEW", PermissionAction.View)]
[Route("Reports")]
public sealed class ReportsController : Controller
{
    private readonly IReportRepositoryFactory _repos;
    private readonly ITenantContext _tenant;

    public ReportsController(
        IReportRepositoryFactory repos,
        ITenantContext tenant)
    {
        _repos = repos;
        _tenant = tenant;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Inventory")]
    public async Task<IActionResult> Inventory(CancellationToken ct = default)
    {
        var repo = _repos.For(_tenant.RequireTenantId());

        // 5 reads — all independent, but the connection serialises them
        // so they go in sequence. Tight enough at <100 rows per slice
        // that a Task.WhenAll detour adds churn without latency wins.
        var vm = new InventoryReportViewModel
        {
            Summary          = await repo.GetInventorySummaryAsync(ct),
            StockByWarehouse = await repo.GetStockByWarehouseAsync(ct),
            AgingBuckets     = await repo.GetStockAgingBucketsAsync(ct),
            TopProducts      = await repo.GetTopProductsByQuantityAsync(limit: 10, ct),
            SlowMovers       = await repo.GetSlowMoversAsync(daysThreshold: 60, limit: 20, ct),
            SnapshotAt       = DateTime.UtcNow,
        };

        return View(vm);
    }

    [HttpGet("Orders")]
    public async Task<IActionResult> Orders(string? range = null, CancellationToken ct = default)
    {
        var (fromUtc, toUtc, label) = DateRangePreset.Resolve(range);
        var repo = _repos.For(_tenant.RequireTenantId());

        var vm = new OrderAnalyticsViewModel
        {
            OrdersByStatus   = await repo.GetOrdersByStatusAsync(fromUtc, toUtc, ct),
            OrdersByDate     = await repo.GetOrdersByDateAsync(fromUtc, toUtc, ct),
            TopCustomers     = await repo.GetTopCustomersAsync(fromUtc, toUtc, limit: 10, ct),
            FulfillmentCycle = await repo.GetFulfillmentCycleAsync(fromUtc, toUtc, ct),
            Preset           = DateRangePreset.NormalisePreset(range),
            PresetLabel      = label,
            FromUtc          = fromUtc,
            ToUtc            = toUtc,
        };

        return View(vm);
    }

    [HttpGet("Kpis")]
    public IActionResult Kpis() => View();
}
