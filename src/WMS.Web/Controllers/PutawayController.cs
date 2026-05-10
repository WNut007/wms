using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Inbound;
using WMS.Common.Auth;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inventory;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Controllers;

// Phase 20 — Mobile Putaway PWA. Replaces Phase 1's single-page
// PutawayController (typed-codes form) with a queue → per-task page →
// confirm / override → bounce-to-queue UX. Mirrors Phase 18 receive
// and Phase 19 mobile pack patterns.
//
// Surfaces:
//   GET  /putaway              — queue (Stock at Receiving/Staging zones,
//                                FIFO oldest first)
//   GET  /putaway/{stockId}    — task page (item card + suggested
//                                location hero + override scan area)
//   POST /putaway/submit/{id}  — calls IPutawayService.PutawayStockAsync
//                                (atomic source→dest move + paired
//                                StockMovements rows per ADR-014)
//
// Audit findings (per Phase 20 audit, applied silently):
// - The spec assumed `master.Locations.IsStaging` flag — does not
//   exist. Reality: `master.Zones.Type IN ('Receiving','Staging')`.
//   Same shape of spec rename as Phase 18 (IsSerialTracked →
//   TrackingMethod) and Phase 19 (LotOnly → Lot). 3rd instance.
// - No PutawayTask header/lines table — queue is derived from Stock
//   sitting at staging zones. No migration this phase.
// - No suggested-location service existed — built inline as a
//   StockRepository read method (same-product-nearby + BinRank).
//
// Manifest: /putaway/manifest.json (scope=/putaway/, theme #534AB7).
// Layout: _MobileLayout via /putaway _ViewStart.
//
// Spec: docs/mockups/mobile-specs/phase-20-mobile-putaway-spec.md
//       (Path A corrections appended in T3)
[Authorize]
[Route("putaway")]
public sealed class PutawayController : Controller
{
    private readonly IPutawayService _putawayService;
    private readonly IStockRepositoryFactory _stockRepos;
    private readonly ITenantConnectionFactory _tenantConn;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public PutawayController(
        IPutawayService putawayService,
        IStockRepositoryFactory stockRepos,
        ITenantConnectionFactory tenantConn,
        ITenantContext tenant,
        ICurrentUser currentUser)
    {
        _putawayService = putawayService;
        _stockRepos = stockRepos;
        _tenantConn = tenantConn;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    // GET /putaway — queue. Stock at Receiving/Staging-zone locations
    // in operator's current warehouse, FIFO. Empty result → "all
    // caught up" empty state. Aged badge (>24h waiting) computed
    // client-side from CreatedAt.
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (_currentUser.WarehouseId is not { } warehouseId)
            return RedirectToAction("SelectWarehouse", "Auth");

        var rows = await _stockRepos.For(_tenant.RequireTenantId())
            .GetPutawayQueueAsync(warehouseId, ct);

        ViewBag.PutawayMessage = TempData["PutawayMessage"] as string;
        ViewBag.PutawayError   = TempData["PutawayError"]   as string;
        return View(rows);
    }

    // GET /putaway/{stockId} — task page. Loads the Stock entity (for
    // 6-tuple) + queue row (for display) + suggested target location.
    // 404 when the row is missing (operator hit a stale URL after
    // someone else put it away) or when the Stock has been drained
    // to zero.
    [HttpGet("{stockId:guid}")]
    public async Task<IActionResult> Task(Guid stockId, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is not { } warehouseId)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        var repo = _stockRepos.For(tenantId);

        var stock = await repo.GetByIdAsync(stockId, ct);
        if (stock is null || stock.QuantityOnHand <= 0m)
            return NotFound();

        // Re-query the queue to find the matching display row. Cheap
        // (small queue at any given moment) and avoids a parallel
        // single-row JOIN-rich SELECT.
        var queue = await repo.GetPutawayQueueAsync(warehouseId, ct);
        var row = queue.FirstOrDefault(r => r.StockId == stockId);
        if (row is null) return NotFound();

        var suggestion = await repo.GetSuggestedPutawayLocationAsync(
            warehouseId, stock.ProductId, ct);

        ViewBag.Suggestion     = suggestion;
        ViewBag.PutawayMessage = TempData["PutawayMessage"] as string;
        ViewBag.PutawayError   = TempData["PutawayError"]   as string;
        return View(row);
    }

    // POST /putaway/submit/{stockId} — calls PutawayService.
    // ToLocationCode override wins when supplied; otherwise the
    // suggested-location call result is the target. Quantity is
    // operator-confirmed (default = full OnHand on the form).
    //
    // Bounce-to-queue on success. Service exceptions (insufficient
    // stock, same source/dest) → bounce back to task with error.
    [HttpPost("submit/{stockId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid stockId, MobilePutawaySubmitViewModel vm, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is not { } warehouseId)
            return RedirectToAction("SelectWarehouse", "Auth");

        if (vm.Quantity <= 0m)
        {
            TempData["PutawayError"] = "Quantity must be positive.";
            return RedirectToAction(nameof(Task), new { stockId });
        }

        var tenantId = _tenant.RequireTenantId();
        var stockRepo = _stockRepos.For(tenantId);

        var stock = await stockRepo.GetByIdAsync(stockId, ct);
        if (stock is null) return NotFound();

        // Resolve target — operator override wins; suggestion is the
        // implicit fallback. Both go through the same warehouse-scoped
        // location lookup (IsActive=1, Status='Active').
        Guid? toLocationId;
        if (!string.IsNullOrWhiteSpace(vm.ToLocationCode))
        {
            toLocationId = await ResolveLocationIdAsync(
                tenantId, warehouseId, vm.ToLocationCode.Trim(), ct);
            if (toLocationId is null)
            {
                TempData["PutawayError"] =
                    $"Location '{vm.ToLocationCode}' not found in this warehouse.";
                return RedirectToAction(nameof(Task), new { stockId });
            }
        }
        else
        {
            var suggestion = await stockRepo.GetSuggestedPutawayLocationAsync(
                warehouseId, stock.ProductId, ct);
            if (suggestion is null)
            {
                TempData["PutawayError"] =
                    "No suggested storage location available — scan a target bin.";
                return RedirectToAction(nameof(Task), new { stockId });
            }
            toLocationId = suggestion.LocationId;
        }

        var fromKey = new StockKey(
            LocationId: stock.LocationId,
            ProductId: stock.ProductId,
            LotId: stock.LotId,
            PalletId: stock.PalletId,
            OwnerId: stock.OwnerId,
            UomId: stock.UomId);

        try
        {
            var result = await _putawayService.PutawayStockAsync(
                tenantId,
                new PutawayRequest(fromKey, toLocationId.Value, vm.Quantity),
                _currentUser.UserId,
                ct);

            TempData["PutawayMessage"] =
                $"Moved {vm.Quantity:N2} units · destination OnHand now {result.Destination.QuantityOnHand:N2}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["PutawayError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { stockId });
        }
    }

    // Tiny inline location resolver — same pattern as Phase 18
    // ReceiveController's. Single-row lookup; no point spinning up an
    // ILocationRepository surface for one consumer.
    private async Task<Guid?> ResolveLocationIdAsync(
        Guid tenantId, Guid warehouseId, string code, CancellationToken ct)
    {
        using var conn = _tenantConn.CreateConnection(tenantId);
        return await conn.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT Id FROM master.Locations " +
                "WHERE WarehouseId = @warehouseId AND Code = @code " +
                "  AND IsActive = 1 AND Status = 'Active'",
                new { warehouseId, code },
                cancellationToken: ct));
    }
}
