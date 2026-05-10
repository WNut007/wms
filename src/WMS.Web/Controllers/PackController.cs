using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Outbound;
using WMS.Web.Models.Outbound;

namespace WMS.Web.Controllers;

// Phase 19 — Mobile Pack PWA. Mirrors Phase 18 mobile receive
// (queue → per-task page → submit/cancel → bounce-to-queue) and
// Phase 16 mobile picker.
//
// Path D from the spec audit: per-line card pattern, NO scan UI.
// The Phase 19 spec's "smart scan" + "carton hero card" + "GREEN
// submit" + multi-scenario validation chain were aspirational —
// the backend (PackTaskService.SubmitAsync) is batch-submit, not
// scan-incremental, and there's no serial inventory schema today
// (TD-040/TD-042/TD-043 cover the deferred work as Phase 19.5).
//
// Surfaces:
//   GET  /pack             — queue (PackTasks Pending only; Packed +
//                            Cancelled are terminal, /PackTasks list
//                            covers them desktop-side)
//   GET  /pack/{taskId}    — task page (per-line cards + carton strip)
//   POST /pack/submit/{id} — same IPackTaskService.SubmitAsync entry
//                            as desktop PackTasks/Submit
//   POST /pack/cancel/{id} — same IPackTaskService.CancelAsync entry
//
// Manifest: /pack/manifest.json (scope=/pack/, start_url=/pack).
// Layout: _MobileLayout via /pack _ViewStart.
//
// Design system: docs/mockups/mobile-specs/mobile-design-system.md
// Phase spec:    docs/mockups/mobile-specs/phase-19-mobile-pack-spec.md
//                (Path D deferrals appended in T3)
[Authorize]
[Route("pack")]
public sealed class PackController : Controller
{
    private readonly IPackTaskRepositoryFactory _packRepos;
    private readonly ISalesOrderRepositoryFactory _soRepos;
    private readonly IProductRepositoryFactory _productRepos;
    private readonly IBoxTypeRepositoryFactory _boxTypeRepos;
    private readonly IPackTaskService _service;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public PackController(
        IPackTaskRepositoryFactory packRepos,
        ISalesOrderRepositoryFactory soRepos,
        IProductRepositoryFactory productRepos,
        IBoxTypeRepositoryFactory boxTypeRepos,
        IPackTaskService service,
        ITenantContext tenant,
        ICurrentUser currentUser)
    {
        _packRepos = packRepos;
        _soRepos = soRepos;
        _productRepos = productRepos;
        _boxTypeRepos = boxTypeRepos;
        _service = service;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    // GET /pack — queue. Pending pack tasks only, FIFO by GeneratedAt
    // ASC (oldest first — operator clears the backlog). Page size 50
    // matches Phase 18 receive — generous for a single packer session.
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        var repo = _packRepos.For(tenantId);

        var pending = await repo.GetPagedAsync(new PackTaskFilter(
            Page: 1, PageSize: 50,
            Status: "Pending",
            SortBy: "generatedAt", SortDesc: false), ct);

        return View(pending.Items);
    }

    // GET /pack/{taskId} — task page. Loads PackTaskDetail + bulk
    // product metadata (one round-trip via GetMetaByIdsAsync). Non-
    // Pending tasks render NotFound — operator hits the desktop
    // /PackTasks/Detail/{id} page for terminal tasks. Same pattern as
    // Phase 18 receive's Closed/Cancelled-PO guard.
    [HttpGet("{taskId:guid}")]
    public async Task<IActionResult> Task(Guid taskId, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        var detail = await _packRepos.For(tenantId).GetByIdAsync(taskId, ct);
        if (detail is null) return NotFound();

        if (detail.Header.Status != "Pending")
            return NotFound();

        var so = await _soRepos.For(tenantId).GetByIdAsync(detail.Header.SalesOrderId, ct);

        var productMeta = await _productRepos.For(tenantId).GetMetaByIdsAsync(
            detail.Lines.Select(l => l.ProductId), ct);

        var boxTypes = await _boxTypeRepos.For(tenantId).GetActiveAsync(ct);

        ViewBag.SoNumber    = so?.Header.SoNumber ?? "—";
        ViewBag.ProductMeta = productMeta;
        ViewBag.BoxTypes    = boxTypes;
        ViewBag.PackMessage = TempData["PackMessage"] as string;
        ViewBag.PackError   = TempData["PackError"]   as string;
        return View(detail);
    }

    // POST /pack/submit/{taskId} — projects the form into the
    // existing SubmitPackTaskRequest + delegates to PackTaskService.
    // Reuses the desktop PackTasks/Submit ViewModel verbatim
    // (SubmitPackTaskViewModel + PackedLineRow). Bounce-to-queue on
    // success (mobile UX — operator grabs the next task).
    //
    // Serial-tracked guard (TD-043): if any line's product is
    // LotAndSerial, reject the whole submit with "use desktop"
    // message — same shape as Phase 18 receive's TD-040 guard.
    [HttpPost("submit/{taskId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid taskId, SubmitPackTaskViewModel vm, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var tenantId = _tenant.RequireTenantId();
        var detail = await _packRepos.For(tenantId).GetByIdAsync(taskId, ct);
        if (detail is null) return NotFound();

        // Serial-tracked product guard — same shape as Phase 18
        // receive submit's TrackingMethod check. Keeps mobile pack
        // free of serial-aware plumbing until the schema lands.
        var productMeta = await _productRepos.For(tenantId).GetMetaByIdsAsync(
            detail.Lines.Select(l => l.ProductId), ct);

        var serialLine = detail.Lines.FirstOrDefault(l =>
            productMeta.TryGetValue(l.ProductId, out var meta)
            && meta.TrackingMethod == "LotAndSerial");
        if (serialLine is not null)
        {
            var meta = productMeta[serialLine.ProductId];
            TempData["PackError"] =
                $"Line {serialLine.LineNumber} ({meta.Code}) is serial-tracked. " +
                "Use the desktop Pack form (mobile serial-aware pack is Phase 19.5 / TD-043).";
            return RedirectToAction(nameof(Task), new { taskId });
        }

        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var entries = (vm.Lines ?? new List<PackedLineRow>())
                .Select(l => new PackedLineEntry(
                    LineId: l.LineId,
                    PackedQuantity: l.PackedQuantity,
                    LineStatus: l.LineStatus,
                    ShortPackReason: string.IsNullOrWhiteSpace(l.ShortPackReason)
                        ? null : l.ShortPackReason.Trim(),
                    Notes: string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                .ToList();

            var request = new SubmitPackTaskRequest(
                PackTaskId: taskId,
                Lines: entries,
                BoxTypeId: vm.BoxTypeId == Guid.Empty ? null : vm.BoxTypeId,
                WeightKg: vm.WeightKg,
                CartonNotes: vm.CartonNotes);

            var result = await _service.SubmitAsync(tenantId, request, requesterId, ct);
            TempData["PackMessage"] =
                $"Pack submitted — carton {result.CartonNumber} " +
                $"({result.FullyPackedLineCount} full, {result.ShortPackedLineCount} short, " +
                $"{result.SkippedLineCount} skipped). SO is now {result.SalesOrderStatus}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["PackError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { taskId });
        }
    }

    // POST /pack/cancel/{taskId} — operator backs out via native
    // window.prompt(). Required reason gates at the controller (3-char
    // min) since mobile bypasses FluentValidation (no model-bound VM).
    // Idempotent on already-Cancelled (service returns false → friendly
    // banner). Bounce-to-queue per mobile UX.
    [HttpPost("cancel/{taskId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid taskId, string reason, CancellationToken ct)
    {
        if (_currentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length < 3)
        {
            TempData["PackError"] = "Cancel reason is required (at least 3 characters).";
            return RedirectToAction(nameof(Task), new { taskId });
        }

        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.CancelAsync(tenantId, taskId, trimmed, requesterId, ct);
            TempData["PackMessage"] = changed
                ? "Pack task cancelled — SO state unchanged (still Picked or PartiallyPicked)."
                : "Pack task was already cancelled.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            TempData["PackError"] = ex.Message;
            return RedirectToAction(nameof(Task), new { taskId });
        }
    }
}
