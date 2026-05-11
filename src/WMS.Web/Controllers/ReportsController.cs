using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Reports;
using WMS.Web.Filters;
using WMS.Web.Services.Reports;
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
    public async Task<IActionResult> Inventory(CancellationToken ct = default) =>
        View(await BuildInventoryAsync(ct));

    [HttpGet("Orders")]
    public async Task<IActionResult> Orders(string? range = null, CancellationToken ct = default) =>
        View(await BuildOrdersAsync(range, ct));

    [HttpGet("Kpis")]
    public async Task<IActionResult> Kpis(string? range = null, CancellationToken ct = default) =>
        View(await BuildKpisAsync(range, ct));

    // ── Excel exports (T5) ─────────────────────────────────────────────

    [HttpGet("ExportInventory")]
    public async Task<IActionResult> ExportInventory(CancellationToken ct = default)
    {
        var vm = await BuildInventoryAsync(ct);
        var (bytes, fileName, contentType) = ReportExcelExporter.ExportInventory(vm);
        return File(bytes, contentType, fileName);
    }

    [HttpGet("ExportOrders")]
    public async Task<IActionResult> ExportOrders(string? range = null, CancellationToken ct = default)
    {
        var vm = await BuildOrdersAsync(range, ct);
        var (bytes, fileName, contentType) = ReportExcelExporter.ExportOrders(vm);
        return File(bytes, contentType, fileName);
    }

    [HttpGet("ExportKpis")]
    public async Task<IActionResult> ExportKpis(string? range = null, CancellationToken ct = default)
    {
        var vm = await BuildKpisAsync(range, ct);
        var (bytes, fileName, contentType) = ReportExcelExporter.ExportKpis(vm);
        return File(bytes, contentType, fileName);
    }

    // ── ViewModel builders ─────────────────────────────────────────────
    //
    // Extracted so Export* actions reuse the same query bundle the view
    // actions consume — single source of truth per report.

    private async Task<InventoryReportViewModel> BuildInventoryAsync(CancellationToken ct)
    {
        var repo = _repos.For(_tenant.RequireTenantId());
        return new InventoryReportViewModel
        {
            Summary          = await repo.GetInventorySummaryAsync(ct),
            StockByWarehouse = await repo.GetStockByWarehouseAsync(ct),
            AgingBuckets     = await repo.GetStockAgingBucketsAsync(ct),
            TopProducts      = await repo.GetTopProductsByQuantityAsync(limit: 10, ct),
            SlowMovers       = await repo.GetSlowMoversAsync(daysThreshold: 60, limit: 20, ct),
            SnapshotAt       = DateTime.UtcNow,
        };
    }

    private async Task<OrderAnalyticsViewModel> BuildOrdersAsync(string? range, CancellationToken ct)
    {
        var (fromUtc, toUtc, label) = DateRangePreset.Resolve(range);
        var repo = _repos.For(_tenant.RequireTenantId());
        return new OrderAnalyticsViewModel
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
    }

    private async Task<KpiReportViewModel> BuildKpisAsync(string? range, CancellationToken ct)
    {
        var (fromUtc, toUtc, label) = DateRangePreset.Resolve(range);
        var repo = _repos.For(_tenant.RequireTenantId());
        return new KpiReportViewModel
        {
            PicksByDay         = await repo.GetPicksByDayAsync(fromUtc, toUtc, ct),
            PacksByDay         = await repo.GetPacksByDayAsync(fromUtc, toUtc, ct),
            CycleCountVariance = await repo.GetCycleCountVarianceAsync(fromUtc, toUtc, ct),
            OnTimeShipping     = await repo.GetOnTimeShippingAsync(fromUtc, toUtc, ct),
            TopPickers         = await repo.GetTopPickersAsync(fromUtc, toUtc, limit: 10, ct),
            Preset             = DateRangePreset.NormalisePreset(range),
            PresetLabel        = label,
            FromUtc            = fromUtc,
            ToUtc              = toUtc,
        };
    }
}
