using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Counts;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Counts;
using WMS.Web.Models.Counts;

namespace WMS.Web.Controllers;

// Phase 21 — Mobile Cycle Count PWA. Mirrors Phase 18 receive +
// Phase 19 mobile pack + Phase 20 mobile putaway patterns.
//
// All-lines-on-one-page UX (per Phase 18-20 mental model). Spec
// describes a per-location wizard ("Location 16 of 24") — Phase 21.5
// candidate (TD-044). Operator pre-walks the aisle physically + types
// quantities at the end, not card-by-card with the device.
//
// Surfaces:
//   GET  /count             — queue (Counting + Review sections)
//   GET  /count/{sessionId} — task page (per-line cards, single submit)
//   POST /count/save/{id}   — IcycleCountService.SaveCountedQuantitiesAsync
//                             (draft save; bounces back to task page)
//   POST /count/submit/{id} — Save + SubmitForReviewAsync
//                             (Counting → Review; bounces to queue)
//   POST /count/cancel/{id} — IcycleCountService.CancelAsync
//                             (required reason via window.prompt)
//
// Apply step (Review → Applied + Stock writes) is desktop-only per
// spec MVP — separation of duties enforced at service layer
// (counter ≠ approver).
//
// Manifest: /count/manifest.json (scope=/count/, theme #534AB7).
// Layout:   _MobileLayout via /count _ViewStart.
//
// Spec: docs/mockups/mobile-specs/phase-21-mobile-cycle-count-spec.md
//       (Implementation Notes appendix in T3)
[Authorize]
[Route("count")]
public sealed class CountController : Controller
{
    private readonly ICycleCountService _service;
    private readonly ICycleCountRepositoryFactory _repos;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public CountController(
        ICycleCountService service,
        ICycleCountRepositoryFactory repos,
        ITenantContext tenant,
        ICurrentUser currentUser)
    {
        _service = service;
        _repos = repos;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    // GET /count — queue. Two paged calls (Counting + Review). Page
    // size 50 per status. Counting on top (operator's actionable
    // sessions); Review below (read-only — desktop approves).
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var repo = _repos.For(_tenant.RequireTenantId());

        var counting = await repo.GetPagedAsync(new CycleCountFilter(
            Page: 1, PageSize: 50,
            Status: "Counting",
            SortBy: "startedAt", SortDesc: false), ct);
        var review = await repo.GetPagedAsync(new CycleCountFilter(
            Page: 1, PageSize: 50,
            Status: "Review",
            SortBy: "startedAt", SortDesc: false), ct);

        ViewBag.CountMessage = TempData["CountMessage"] as string;
        ViewBag.CountError   = TempData["CountError"]   as string;
        ViewBag.ReviewRows   = review.Items;
        return View(counting.Items);
    }

    // GET /count/{sessionId} — task page. Loads the full session.
    // 404 when missing or in a non-actionable state. Counting +
    // Review both render (Counting = editable; Review = read-only
    // summary). Applied + Cancelled → 404 (operator hits the desktop
    // Detail page for terminal records).
    [HttpGet("{sessionId:guid}")]
    public async Task<IActionResult> Task(Guid sessionId, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        var detail = await _service.GetByIdAsync(tenantId, sessionId, ct);
        if (detail is null) return NotFound();
        if (detail.Header.Status is not "Counting" and not "Review")
            return NotFound();

        // Richer projection (resolved Product / Location / UoM /
        // Owner / Lot / Pallet codes) — drives the per-line card
        // render without per-row lookups. Same shape as Phase 18
        // ReceiveController's bulk product meta fetch.
        var lineRows = await _repos.For(tenantId).GetLineRowsByIdAsync(sessionId, ct);

        ViewBag.LineRows     = lineRows;
        ViewBag.CountMessage = TempData["CountMessage"] as string;
        ViewBag.CountError   = TempData["CountError"]   as string;
        return View(detail);
    }

    // POST /count/save/{sessionId} — bulk per-line save. Mode = "draft"
    // bounces back to task page (operator continues counting); mode =
    // "submit" follows up with SubmitForReviewAsync (Counting → Review)
    // and bounces to queue.
    [HttpPost("save/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        Guid sessionId, MobileSaveCountViewModel vm, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var updates = (vm.Lines ?? new List<CountLineEntry>())
                .Select(l => new CountLineUpdate(
                    LineId: l.LineId,
                    CountedQuantity: l.CountedQuantity,
                    LineStatus: l.LineStatus,
                    Notes: string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                .ToList();

            await _service.SaveCountedQuantitiesAsync(
                _tenant.RequireTenantId(), sessionId, updates, requesterId, ct);

            TempData["CountMessage"] = $"Saved {updates.Count} line(s).";
            return RedirectToAction(nameof(Task), new { sessionId });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["CountError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { sessionId });
        }
    }

    // POST /count/submit/{sessionId} — save + Counting → Review.
    // SubmitForReviewAsync is idempotent at SQL level via WHERE Status=
    // 'Counting'; returns false if already-Review (controller surfaces
    // a friendly banner). On success, bounces to queue (operator grabs
    // next session).
    [HttpPost("submit/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid sessionId, MobileSaveCountViewModel vm, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");
        var tenantId = _tenant.RequireTenantId();

        try
        {
            // Save before submit so any operator edits in this session
            // land before the state-flip (Counting → Review locks
            // further per-line edits).
            var updates = (vm.Lines ?? new List<CountLineEntry>())
                .Select(l => new CountLineUpdate(
                    LineId: l.LineId,
                    CountedQuantity: l.CountedQuantity,
                    LineStatus: l.LineStatus,
                    Notes: string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                .ToList();

            if (updates.Count > 0)
            {
                await _service.SaveCountedQuantitiesAsync(
                    tenantId, sessionId, updates, requesterId, ct);
            }

            var changed = await _service.SubmitForReviewAsync(
                tenantId, sessionId, requesterId, ct);

            TempData["CountMessage"] = changed
                ? "Submitted for review — desktop approver will apply variances."
                : "Session was already in Review.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["CountError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { sessionId });
        }
    }

    // POST /count/cancel/{sessionId} — window.prompt-driven reason
    // capture. Controller-level 3-char min reason gate (mobile bypasses
    // FluentValidation since reason comes via prompt(), not a model-
    // bound VM — same shape as Phase 19 + 20). Idempotent on already-
    // Cancelled.
    [HttpPost("cancel/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid sessionId, string reason, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length < 3)
        {
            TempData["CountError"] = "Cancel reason is required (at least 3 characters).";
            return RedirectToAction(nameof(Task), new { sessionId });
        }

        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.CancelAsync(
                _tenant.RequireTenantId(), sessionId, trimmed, requesterId, ct);
            TempData["CountMessage"] = changed
                ? "Cycle count cancelled."
                : "Session was already cancelled.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            TempData["CountError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { sessionId });
        }
    }
}
