using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inventory;

namespace WMS.Web.Controllers;

// Phase 22 — Mobile Locate PWA. Read-only utility (find an item or
// a location). Closes the mobile suite (6 of 6 mobile ops shipped).
//
// Surfaces:
//   GET /locate                  — search entry (input + scan area +
//                                  client-side recent list via localStorage)
//   GET /locate/search?q={code}  — smart search, redirects to
//                                  /locate/item/{id} or /locate/loc/{id};
//                                  not-found bounces back with banner
//   GET /locate/item/{productId} — multi-location view per product
//   GET /locate/loc/{locationId} — items at location
//
// Smart search drops serial detection — no serial schema today
// (TD-043 family, Phase 19.5 bundle). Product OR location only.
//
// Recent searches = client-side localStorage only (no backend table).
// Favorites deferred (TD).
//
// Manifest: /locate/manifest.json (scope=/locate/, theme #534AB7).
// Layout:   _MobileLayout via /locate _ViewStart.
//
// Spec: docs/mockups/mobile-specs/phase-22-mobile-locate-spec.md
//       (Implementation Notes appendix in T3)
[Authorize]
[Route("locate")]
public sealed class LocateController : Controller
{
    private readonly IStockRepositoryFactory _stockRepos;
    private readonly ITenantConnectionFactory _tenantConn;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public LocateController(
        IStockRepositoryFactory stockRepos,
        ITenantConnectionFactory tenantConn,
        ITenantContext tenant,
        ICurrentUser currentUser)
    {
        _stockRepos = stockRepos;
        _tenantConn = tenantConn;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    // GET /locate — search entry view. No data load on the server;
    // recent searches live in client-side localStorage.
    [HttpGet("")]
    public IActionResult Index()
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        ViewBag.LocateMessage = TempData["LocateMessage"] as string;
        ViewBag.LocateError   = TempData["LocateError"]   as string;
        return View();
    }

    // GET /locate/search?q=... — smart search. Tries Product code,
    // then Location code (warehouse-scoped). Returns redirect on
    // hit; not-found bounces back to /locate with a banner. Skips
    // serial detection (TD-043).
    [HttpGet("search")]
    public async Task<IActionResult> Search(string? q, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is not { } warehouseId)
            return RedirectToAction("SelectWarehouse", "Auth");

        var trimmed = (q ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return RedirectToAction(nameof(Index));

        var tenantId = _tenant.RequireTenantId();
        using var conn = _tenantConn.CreateConnection(tenantId);

        // Try product first (prefix-or-exact on Code; SKUs typically
        // are scanned exactly). Active products only.
        var productId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT TOP (1) Id FROM master.Products " +
                "WHERE Code = @code AND Status = 'Active'",
                new { code = trimmed },
                cancellationToken: ct));
        if (productId is { } pid)
            return RedirectToAction(nameof(Item), new { productId = pid });

        // Then location (current warehouse only).
        var locationId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT TOP (1) Id FROM master.Locations " +
                "WHERE WarehouseId = @warehouseId AND Code = @code " +
                "  AND IsActive = 1 AND Status = 'Active'",
                new { warehouseId, code = trimmed },
                cancellationToken: ct));
        if (locationId is { } lid)
            return RedirectToAction(nameof(Loc), new { locationId = lid });

        TempData["LocateError"] =
            $"No product or location matched '{trimmed}'. " +
            "(Serial scanning is Phase 19.5 / TD-043.)";
        return RedirectToAction(nameof(Index));
    }

    // GET /locate/item/{productId} — multi-location view per product.
    // 404 when the product doesn't exist; empty rows render an
    // "out of stock everywhere" empty state in the view.
    [HttpGet("item/{productId:guid}")]
    public async Task<IActionResult> Item(Guid productId, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        using var conn = _tenantConn.CreateConnection(tenantId);

        var product = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Code, string Name, string UomCode)?>(
            new CommandDefinition(
                @"SELECT p.Id, p.Code, p.Name, u.Code AS UomCode
                  FROM master.Products p
                  JOIN master.UnitsOfMeasure u ON u.Id = p.BaseUomId
                  WHERE p.Id = @id",
                new { id = productId },
                cancellationToken: ct));
        if (product is null) return NotFound();

        var rows = await _stockRepos.For(tenantId).GetItemViewAsync(productId, ct);

        ViewBag.ProductCode = product.Value.Code;
        ViewBag.ProductName = product.Value.Name;
        ViewBag.BaseUomCode = product.Value.UomCode;
        ViewBag.LocateMessage = TempData["LocateMessage"] as string;
        ViewBag.LocateError   = TempData["LocateError"]   as string;
        return View(rows);
    }

    // GET /locate/loc/{locationId} — items at location. 404 when the
    // location doesn't exist OR isn't in the operator's current
    // warehouse (cross-warehouse browse blocked at the controller —
    // tenant boundary already enforced by connection scope).
    [HttpGet("loc/{locationId:guid}")]
    public async Task<IActionResult> Loc(Guid locationId, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is not { } warehouseId)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        using var conn = _tenantConn.CreateConnection(tenantId);

        var location = await conn.QuerySingleOrDefaultAsync<(
            Guid Id, string Code, string? Description, Guid WarehouseId,
            string ZoneCode, string ZoneName, string ZoneType, string Status,
            decimal? CapacityVolumeCubicCm, string CapacityPolicy)?>(
            new CommandDefinition(
                @"SELECT loc.Id, loc.Code, loc.Description, loc.WarehouseId,
                         z.Code AS ZoneCode, z.Name AS ZoneName, z.Type AS ZoneType,
                         loc.Status, loc.CapacityVolumeCubicCm, loc.CapacityPolicy
                  FROM master.Locations loc
                  JOIN master.Zones z ON z.Id = loc.ZoneId
                  WHERE loc.Id = @id",
                new { id = locationId },
                cancellationToken: ct));
        if (location is null) return NotFound();
        if (location.Value.WarehouseId != warehouseId) return NotFound();

        var rows = await _stockRepos.For(tenantId).GetLocationViewAsync(locationId, ct);

        ViewBag.LocationCode    = location.Value.Code;
        ViewBag.LocationName    = location.Value.Description ?? location.Value.Code;
        ViewBag.ZoneCode        = location.Value.ZoneCode;
        ViewBag.ZoneName        = location.Value.ZoneName;
        ViewBag.ZoneType        = location.Value.ZoneType;
        ViewBag.LocationStatus  = location.Value.Status;
        ViewBag.CapacityPolicy  = location.Value.CapacityPolicy;
        ViewBag.LocateMessage   = TempData["LocateMessage"] as string;
        ViewBag.LocateError     = TempData["LocateError"]   as string;
        return View(rows);
    }
}
