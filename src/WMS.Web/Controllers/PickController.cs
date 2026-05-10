using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Outbound;
using WMS.DAL.Repositories.Outbound;
using WMS.Web.Models.Outbound;

namespace WMS.Web.Controllers;

// Phase 16 — Mobile Picker PWA. Single-page-per-task UX (mirrors
// the Phase 1 ReceiveController precedent — pragmatic flat form,
// not the 4-tier scan vision from the design docs which is a
// future TD).
//
// Surfaces:
//   GET  /pick           — queue (FIFO list of Pending|InProgress tasks)
//   GET  /pick/{id}      — task page (per-line cards + submit + cancel)
//   POST /pick/submit/{id} — same IPickTaskService.SubmitAsync entry as desktop
//   POST /pick/cancel/{id} — same IPickTaskService.CancelAsync entry as desktop
//
// Manifest: /pick/manifest.json (scope=/pick/, start_url=/pick).
// Layout: _MobileLayout via /pick _ViewStart.
[Authorize]
[Route("pick")]
public sealed class PickController : BaseController
{
    private readonly IPickTaskRepositoryFactory _pickRepos;
    private readonly ISalesOrderRepositoryFactory _soRepos;
    private readonly IPickTaskService _service;

    public PickController(
        IPickTaskRepositoryFactory pickRepos,
        ISalesOrderRepositoryFactory soRepos,
        IPickTaskService service)
    {
        _pickRepos = pickRepos;
        _soRepos = soRepos;
        _service = service;
    }

    // GET /pick — queue. Reuses the desktop list-page DAL but renders
    // mobile cards. Filters to active tasks (Pending|InProgress) and
    // orders FIFO so the oldest open task is at the top.
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (CurrentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = TenantContext.RequireTenantId();
        var repo = _pickRepos.For(tenantId);

        // Two paged calls — Pending FIFO + InProgress FIFO. Smaller
        // page size than the desktop list since mobile screen real
        // estate is tight; the queue is consume-from-top, so 50 is
        // already more than a picker would work in a session.
        var pending = await repo.GetPagedAsync(new PickTaskFilter(
            Page: 1, PageSize: 50,
            Status: "Pending",
            SortBy: "generatedAt", SortDesc: false), ct);
        var inProgress = await repo.GetPagedAsync(new PickTaskFilter(
            Page: 1, PageSize: 50,
            Status: "InProgress",
            SortBy: "generatedAt", SortDesc: false), ct);

        // InProgress at the top — operator likely returning to a task
        // they started; Pending tasks below as the next-up queue.
        var rows = inProgress.Items.Concat(pending.Items).ToList();
        return View(rows);
    }

    // GET /pick/{id} — task page. Loads the PickTaskDetail (header +
    // lines) and renders the per-line cards form. Cancelled / Picked /
    // PartiallyPicked tasks are read-only (operator may still want to
    // see what was done).
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Task(Guid id, CancellationToken ct)
    {
        if (CurrentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = TenantContext.RequireTenantId();
        var detail = await _pickRepos.For(tenantId).GetByIdAsync(id, ct);
        if (detail is null) return NotFound();

        // Resolve SO header for the task page banner (SO# + customer).
        var so = await _soRepos.For(tenantId).GetByIdAsync(detail.Header.SalesOrderId, ct);
        ViewBag.SoNumber = so?.Header.SoNumber ?? "—";

        ViewBag.PickMessage = TempData["PickMessage"] as string;
        ViewBag.PickError   = TempData["PickError"]   as string;
        return View(detail);
    }

    // POST /pick/submit/{id} — projects the form into a Submit-
    // PickTaskRequest and hits the same service method as the desktop
    // form. Service-side ValidateRequestShape enforces the per-line
    // contract; this controller stays thin.
    [HttpPost("submit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid id, SubmitPickTaskViewModel vm, CancellationToken ct)
    {
        var requesterId = CurrentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var entries = vm.Lines
                .Select(l => new PickedLineEntry(
                    LineId: l.LineId,
                    PickedQuantity: l.PickedQuantity,
                    LineStatus: l.LineStatus,
                    ShortPickReason: string.IsNullOrWhiteSpace(l.ShortPickReason)
                        ? null : l.ShortPickReason.Trim(),
                    Notes: string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                .ToList();

            var request = new SubmitPickTaskRequest(
                PickTaskId: id, Lines: entries);

            var result = await _service.SubmitAsync(
                TenantContext.RequireTenantId(), request, requesterId, ct);

            TempData["PickMessage"] = result.TaskStatus == "Picked"
                ? $"Submitted — full pick ({result.FullyPickedLineCount} lines)."
                : $"Submitted — {result.FullyPickedLineCount} full, {result.ShortPickedLineCount} short, {result.SkippedLineCount} skipped.";
            // Mobile UX: bounce back to the queue so the operator
            // grabs the next task instead of staring at a terminal
            // task page.
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["PickError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { id });
        }
    }

    // POST /pick/cancel/{id} — operator cancels the task (e.g. needs
    // a reassignment). Same service entry as desktop. Mobile uses
    // native confirm() rather than a CSS modal, so this endpoint
    // takes the reason inline as a form field.
    [HttpPost("cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid id, string reason, CancellationToken ct)
    {
        var requesterId = CurrentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
        {
            TempData["PickError"] = "Cancellation reason is required (3+ characters).";
            return RedirectToAction(nameof(Task), new { id });
        }

        try
        {
            var changed = await _service.CancelAsync(
                TenantContext.RequireTenantId(), id, reason.Trim(), requesterId, ct);
            TempData["PickMessage"] = changed
                ? "Pick task cancelled."
                : "Pick task was already cancelled.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            TempData["PickError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { id });
        }
    }
}
