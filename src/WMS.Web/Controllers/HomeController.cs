using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;
using WMS.Web.Filters;
using WMS.Web.Models;
using WMS.Web.ViewModels.Home;

namespace WMS.Web.Controllers;

public class HomeController : BaseController
{
    private readonly IPermissionService _permService;

    public HomeController(IPermissionService permService) =>
        _permService = permService;

    // Mock data — Phase 2 dashboard. Real queries + SignalR live feed
    // will replace this in Phase 3.
    [Authorize]
    public IActionResult Index()
    {
        var vm = new DashboardViewModel
        {
            ReceiptsTotal = 142,
            OrdersTotal   = 256,
            LiveFeedData  = new[]
            {
                42, 45, 50, 55, 62, 68, 72, 75, 78, 82, 85, 87,
                85, 82, 80, 78, 75, 72, 68, 65, 60, 55, 50, 45,
            },
            PendingPutaway = new ProgressItem("Pending putaway", 38,  100, "#1F2937"),
            PickedToday    = new ProgressItem("Picked today",   189, 250, "#14B8A6"),
            OrderAccuracy  = new ProgressItem("Order accuracy",  97, 100, "#3B82F6"),
            SlaCompliance  = new ProgressItem("SLA compliance",  60, 100, "#8B5CF6"),
            MetricCards = new[]
            {
                new MetricCard("Receipts",    75, "#5D4FA0", new[] { 65, 68, 70, 72, 75, 73, 75 }, "97%",  "44%"),
                new MetricCard("Stock fill",  79, "#14B8A6", new[] { 75, 76, 78, 79, 80, 79, 79 }, "76%",  "3%"),
                new MetricCard("Putaway TTF", 23, "#3B82F6", new[] { 28, 26, 25, 24, 22, 23, 23 }, "10GB", "10%"),
                new MetricCard("Alerts",      36, "#EC4899", new[] { 40, 38, 35, 34, 36, 37, 36 }, "124",  "40F"),
            },
        };
        return View(vm);
    }

    public IActionResult Privacy() => View();

    // Sample protected endpoint — requires INVENTORY.STOCK : View. The
    // bootstrap admin holds every permission via migration 043, so this
    // endpoint is reachable as soon as login completes. Useful as a
    // smoke target for the permission filter without seeding a second
    // role.
    [RequirePermission("INVENTORY.STOCK", PermissionAction.View)]
    public async Task<IActionResult> Permissions(CancellationToken ct)
    {
        var perms = await _permService.GetForUserAsync(
            CurrentUser.UserId!.Value,
            CurrentUser.TenantId!.Value,
            ct);
        return View(perms);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
