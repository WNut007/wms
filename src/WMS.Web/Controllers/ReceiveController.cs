using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Inbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Controllers;

// Phase 18 (replaces Phase 1's single-page Receiving-ε form) —
// Mobile Receive PWA. Mirrors the Phase 16 mobile picker pattern:
// queue → per-task page → submit/cancel → bounce-to-queue.
//
// Surfaces:
//   GET  /receive            — queue (POs with Open|Receiving status)
//   GET  /receive/{poId}     — task page (per-line cards)
//   POST /receive/submit/{poId} — same IReceivingHeaderService.PostReceivingAsync
//                                  entry as desktop GoodsReceipt
//   POST /receive/cancel/{poId} — operator backs out; no DB state to
//                                  revert (receipts only land on submit)
//
// Manifest: /receive/manifest.json (scope=/receive/, start_url=/receive).
// Layout: _MobileLayout via /receive _ViewStart (already in place from Phase 1).
//
// Design system: docs/mockups/mobile-specs/mobile-design-system.md
// Phase spec: docs/mockups/mobile-specs/phase-18-mobile-receive-spec.md
//
// Serial-tracked products (TrackingMethod = 'LotAndSerial') show a
// "use desktop for serial-tracked products" banner instead of the
// serial entry mode — schema + per-line serial table land in
// Phase 18.5 (TD).
[Authorize]
[Route("receive")]
public sealed class ReceiveController : Controller
{
    private readonly IReceivingHeaderService _receivingService;
    private readonly IPurchaseOrderRepositoryFactory _poRepos;
    private readonly IProductRepositoryFactory _productRepos;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public ReceiveController(
        IReceivingHeaderService receivingService,
        IPurchaseOrderRepositoryFactory poRepos,
        IProductRepositoryFactory productRepos,
        ITenantContext tenant,
        ICurrentUser currentUser)
    {
        _receivingService = receivingService;
        _poRepos = poRepos;
        _productRepos = productRepos;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    // GET /receive — queue. Two paged calls (Open FIFO + Receiving
    // FIFO via the existing PurchaseOrderRepository) merged with
    // Receiving on top — operator likely returning to a partially-
    // received PO. Page size 50 per status — generous for a single
    // receiver session.
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        var repo = _poRepos.For(tenantId);

        var open = await repo.GetPagedAsync(new PurchaseOrderFilter(
            Page: 1, PageSize: 50,
            Status: "Open",
            SortBy: "expectedDate", SortDesc: false), ct);
        var receiving = await repo.GetPagedAsync(new PurchaseOrderFilter(
            Page: 1, PageSize: 50,
            Status: "Receiving",
            SortBy: "expectedDate", SortDesc: false), ct);

        var rows = receiving.Items.Concat(open.Items).ToList();
        return View(rows);
    }

    // GET /receive/{poId} — task page. Loads the PO header + lines
    // and surfaces per-line cards keyed for entry. Closed/Cancelled
    // POs render NotFound — operator shouldn't be entering receipts
    // against them via this surface (desktop GoodsReceipt allows
    // edge cases like blind receipts, mobile keeps it simple).
    [HttpGet("{poId:guid}")]
    public async Task<IActionResult> Task(Guid poId, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        var detail = await _poRepos.For(tenantId).GetByIdAsync(poId, ct);
        if (detail is null) return NotFound();

        if (detail.Header.Status is not "Open" and not "Receiving")
            return NotFound();

        // Bulk product metadata (Code + Name + TrackingMethod) for
        // the per-line cards. One round-trip vs N per-line lookups.
        var productMeta = await _productRepos.For(tenantId).GetMetaByIdsAsync(
            detail.Lines.Select(l => l.ProductId), ct);

        ViewBag.ProductMeta    = productMeta;
        ViewBag.ReceiveMessage = TempData["ReceiveMessage"] as string;
        ViewBag.ReceiveError   = TempData["ReceiveError"]   as string;
        return View(detail);
    }

    // POST /receive/submit/{poId} — projects the form into a
    // PostReceivingRequest and hits the same service entry as the
    // desktop GoodsReceipt form. Lines with ReceivedQty=0 are dropped
    // (operator left them blank; Phase 1's flat form already had
    // this behavior). Lines pointing at LotAndSerial products get
    // skipped with an error banner — serial entry is a Phase 18.5 TD.
    [HttpPost("submit/{poId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid poId, MobileReceiveSubmitViewModel vm, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is not { } warehouseId)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        var po = await _poRepos.For(tenantId).GetByIdAsync(poId, ct);
        if (po is null) return NotFound();

        // Resolve product TrackingMethods once so we can short-circuit
        // submits that include serial-tracked lines (Phase 18.5 will
        // accept them via a serial-aware service surface).
        var productMeta = await _productRepos.For(tenantId).GetMetaByIdsAsync(
            po.Lines.Select(l => l.ProductId), ct);

        var requestLines = new List<PostReceivingLineRequest>();
        var lineNumber = 1;

        foreach (var entry in vm.Lines ?? Enumerable.Empty<MobileReceiveLineEntry>())
        {
            // Operator left this line blank — skip silently.
            if (entry.ReceivedQuantity is null or <= 0) continue;

            var poLine = po.Lines.FirstOrDefault(l => l.Id == entry.PoLineId);
            if (poLine is null)
            {
                TempData["ReceiveError"] = $"Unknown line {entry.PoLineId} on PO.";
                return RedirectToAction(nameof(Task), new { poId });
            }

            // Serial-tracked guard — service shape doesn't accept
            // serials yet (TD-040). Reject the whole submit so the
            // operator doesn't get a half-receipt; they finish on
            // desktop instead.
            if (productMeta.TryGetValue(poLine.ProductId, out var meta)
                && meta.TrackingMethod == "LotAndSerial")
            {
                TempData["ReceiveError"] =
                    $"Line {poLine.LineNumber} ({meta.Code}) is serial-tracked. " +
                    "Use the desktop Goods Receipt form (mobile serial entry is Phase 18.5).";
                return RedirectToAction(nameof(Task), new { poId });
            }

            var lotInfo = string.IsNullOrWhiteSpace(entry.LotNumber)
                ? null
                : new LotInfo(
                    entry.LotNumber.Trim(),
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    entry.ExpiryDate);

            var palletInfo = string.IsNullOrWhiteSpace(entry.PalletNumber)
                ? null
                : new PalletInfo(entry.PalletNumber.Trim());

            // Each receiving line lands at the PO line's product/uom
            // pair. Location: operator's current warehouse default —
            // the spec's MVP doesn't ask the operator to pick a
            // location per line (defer to a future TD if needed).
            // For now, use the warehouse's default receiving location
            // by FK convention (same as Phase 1's resolveLocation
            // path used "RECV" code lookup; here we punt to the PO's
            // implied receiving zone via... actually we need a
            // location). Fall back to operator-provided LocationCode
            // resolved via product's expected put-zone. To keep MVP
            // simple, require a non-null location code per line.
            if (string.IsNullOrWhiteSpace(entry.LocationCode))
            {
                TempData["ReceiveError"] =
                    $"Line {poLine.LineNumber}: location code is required.";
                return RedirectToAction(nameof(Task), new { poId });
            }

            // Inline location resolution — small enough to keep here.
            var locId = await ResolveLocationIdAsync(
                tenantId, warehouseId, entry.LocationCode.Trim(), ct);
            if (locId is null)
            {
                TempData["ReceiveError"] =
                    $"Line {poLine.LineNumber}: location '{entry.LocationCode}' not found.";
                return RedirectToAction(nameof(Task), new { poId });
            }

            requestLines.Add(new PostReceivingLineRequest(
                LineNumber: lineNumber++,
                PurchaseOrderLineId: poLine.Id,
                ProductId: poLine.ProductId,
                UomId: poLine.UomId,
                OwnerId: po.Header.OwnerId,
                LocationId: locId.Value,
                ReceivedQuantity: entry.ReceivedQuantity.Value,
                Lot: lotInfo,
                Pallet: palletInfo));
        }

        if (requestLines.Count == 0)
        {
            TempData["ReceiveError"] = "Enter received quantity on at least one line.";
            return RedirectToAction(nameof(Task), new { poId });
        }

        // Server-side ReceivingNumber assignment — operator doesn't
        // pick one on mobile. RCV-YYYYMMDD-HHmmss-{poId8} is unique
        // enough at single-warehouse cadence; the spec doesn't
        // mandate the format (desktop GoodsReceipt assigns its own).
        var receivingNumber = $"RCV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{poId.ToString("N").Substring(0, 8)}";

        var request = new PostReceivingRequest(
            ReceivingNumber: receivingNumber,
            PurchaseOrderId: poId,
            WarehouseId: warehouseId,
            ReceivedAt: null,
            Notes: vm.Notes,
            Lines: requestLines);

        try
        {
            var result = await _receivingService.PostReceivingAsync(
                tenantId, request, _currentUser.UserId, ct);
            TempData["ReceiveMessage"] =
                $"Received {requestLines.Count} line(s) on {result.Header.ReceivingNumber}.";
            // Mobile UX: bounce to queue (operator grabs next PO).
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["ReceiveError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { poId });
        }
    }

    // POST /receive/cancel/{poId} — operator backs out of the task
    // page. No DB state to revert (receipts only persist on submit;
    // pre-submit data is operator's local form state). The reason
    // field is captured for future audit if a draft-receipt model
    // lands. Idempotent.
    [HttpPost("cancel/{poId:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(Guid poId, string reason)
    {
        TempData["ReceiveMessage"] = string.IsNullOrWhiteSpace(reason)
            ? "Receipt entry discarded."
            : $"Receipt entry discarded: {reason.Trim()}";
        return RedirectToAction(nameof(Index));
    }

    // Tiny inline location resolver — same shape as Phase 1's
    // ResolveLocationAsync. Repo-method extraction would be cleaner
    // but it's a one-call site; keeps the Phase 18 surface small.
    private async Task<Guid?> ResolveLocationIdAsync(
        Guid tenantId, Guid warehouseId, string code, CancellationToken ct)
    {
        // We don't have an injected ILocationRepository here yet (and
        // the existing one's GetActiveByWarehouseAsync returns lookup
        // items, not a per-code lookup). Delegate to the tenant
        // connection for a single-row lookup — same pattern as Phase
        // 1's inline resolvers.
        var connFactory = HttpContext.RequestServices.GetRequiredService<ITenantConnectionFactory>();
        using var conn = connFactory.CreateConnection(tenantId);
        return await Dapper.SqlMapper.QuerySingleOrDefaultAsync<Guid?>(
            conn,
            new Dapper.CommandDefinition(
                "SELECT Id FROM master.Locations " +
                "WHERE WarehouseId = @warehouseId AND Code = @code " +
                "  AND IsActive = 1 AND Status = 'Active'",
                new { warehouseId, code },
                cancellationToken: ct));
    }
}
