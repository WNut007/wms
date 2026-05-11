using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Web.Filters;

namespace WMS.Web.Controllers;

// Phase 23 — first v3.0.0 chapter phase. Reports module top-level
// sidebar entry; landing page with 3 sub-report cards (Inventory /
// Orders / KPIs). Inventory/Orders/Kpis actions get fleshed out in
// T2/T3/T4 with real aggregation queries + ApexCharts; T1 lands
// the routes + skeleton views so navigation works end-to-end.
//
// Permission: single REPORTS.VIEW (seeded by Migration_20260511_033;
// granted to MANAGER by 034; ADMIN gets it via BLL bypass). Per-report
// permission split is a TD.
//
// Excel export endpoints land in T5 (ExcelExporter helper + 3 routes).
[Authorize]
[RequirePermission("REPORTS.VIEW", PermissionAction.View)]
[Route("Reports")]
public sealed class ReportsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Inventory")]
    public IActionResult Inventory() => View();

    [HttpGet("Orders")]
    public IActionResult Orders() => View();

    [HttpGet("Kpis")]
    public IActionResult Kpis() => View();
}
