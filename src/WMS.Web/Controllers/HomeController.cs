using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;
using WMS.Web.Filters;
using WMS.Web.Models;

namespace WMS.Web.Controllers;

public class HomeController : BaseController
{
    private readonly IPermissionService _permService;

    public HomeController(IPermissionService permService) =>
        _permService = permService;

    public IActionResult Index() => View();

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
