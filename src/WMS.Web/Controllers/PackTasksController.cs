using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Outbound;
using WMS.Web.Models.Outbound;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.Services.Outbound;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

// Phase 14D — Pack task execution surface.
//   GET  /PackTasks                — Phase 15A list page (chip counts + table + pagination)
//   GET  /PackTasks/Data           — Phase 15A JSON envelope for Alpine
//   GET  /PackTasks/Detail/{id}    — _DetailLayout w/ inline submit form (Pending) or read-only table (terminal)
//   POST /PackTasks/Submit/{id}    — TX-wrapped commit via PackTaskService.SubmitAsync
//   POST /PackTasks/Cancel/{id}    — pre-Submit reversal via PackTaskService.CancelAsync
[Authorize]
[Route("PackTasks")]
public sealed class PackTasksController : Controller
{
    private readonly IPackTaskRepositoryFactory _packRepos;
    private readonly ISalesOrderRepositoryFactory _soRepos;
    private readonly IBoxTypeRepositoryFactory _boxTypeRepos;
    private readonly IPackTaskService _service;
    private readonly IPackVideoService _videoService;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<CancelPackTaskViewModel> _cancelValidator;

    public PackTasksController(
        IPackTaskRepositoryFactory packRepos,
        ISalesOrderRepositoryFactory soRepos,
        IBoxTypeRepositoryFactory boxTypeRepos,
        IPackTaskService service,
        IPackVideoService videoService,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<CancelPackTaskViewModel> cancelValidator)
    {
        _packRepos = packRepos;
        _soRepos = soRepos;
        _boxTypeRepos = boxTypeRepos;
        _service = service;
        _videoService = videoService;
        _tenant = tenant;
        _currentUser = currentUser;
        _cancelValidator = cancelValidator;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string sortBy = "generatedAt",
        bool sortDesc = true,
        CancellationToken ct = default)
    {
        var filter = new PackTaskFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: PackTaskStatusMapper.FromWire(status),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _packRepos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(r => new
            {
                id              = r.Id,
                packNumber      = r.PackNumber,
                salesOrderId    = r.SalesOrderId,
                soNumber        = r.SoNumber,
                customerCode    = r.CustomerCode,
                customerName    = r.CustomerName,
                status          = PackTaskStatusMapper.ToWire(r.Status),
                statusLabel     = r.Status,
                lineCount       = r.LineCount,
                generatedAt     = r.GeneratedAt,
                generatedRelative = RelativeTime.Format(r.GeneratedAt),
                generatedByName = r.GeneratedByName,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all       = counts.All,
                pending   = counts.Pending,
                packed    = counts.Packed,
                cancelled = counts.Cancelled,
            },
        });
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var detail = await _packRepos.For(tenantId).GetByIdAsync(id, ct);
        if (detail is null) return NotFound();

        var h = detail.Header;
        var lines = detail.Lines;
        var carton = detail.Carton;

        // SO header for the Overview block (SoNumber + Customer name).
        var so = await _soRepos.For(tenantId).GetByIdAsync(h.SalesOrderId, ct);

        // BoxType lookup — populates the dropdown on Pending; surfaces
        // the resolved code on terminal states (carton already created).
        var boxTypes = await _boxTypeRepos.For(tenantId).GetActiveAsync(ct);

        var isPending   = h.Status == "Pending";
        var isPacked    = h.Status == "Packed";
        var isCancelled = h.Status == "Cancelled";
        var isTerminal  = isPacked || isCancelled;

        // Phase 17 — fetch the latest video for the playback link
        // when status=Packed. Pending tasks can't have videos
        // (UploadAsync rejects); Cancelled tasks shouldn't either,
        // so we only check on Packed.
        var latestVideo = isPacked
            ? await _videoService.GetLatestForPackTaskAsync(tenantId, id, ct)
            : null;

        var canSubmit = isPending;
        var canCancel = isPending;

        var statusVariant = PackTaskStatusMapper.ToBadgeVariant(h.Status);

        var totalPicked = lines.Sum(l => l.PickedQuantity);
        var totalPacked = lines.Sum(l => l.PackedQuantity ?? 0m);
        var fullyPackedLines = lines.Count(l =>
            l.LineStatus == "Packed"
            && l.PackedQuantity.HasValue
            && l.PackedQuantity.Value == l.PickedQuantity);
        var shortPackedLines = lines.Count(l =>
            l.LineStatus == "Packed"
            && l.PackedQuantity.HasValue
            && l.PackedQuantity.Value < l.PickedQuantity);
        var skippedLines = lines.Count(l => l.LineStatus == "Skipped");

        // "Packed" stat tile colour: green when fully packed, amber on
        // any short or skip, neutral pre-submit.
        string? packedColor = null;
        if (isPacked && shortPackedLines == 0 && skippedLines == 0) packedColor = "#0F6E56";
        else if (isPacked) packedColor = "#854F0B";

        var vm = new DetailPageViewModel
        {
            EntityType = "PackTask",
            EntityId = id.ToString(),
            Title = h.PackNumber,
            Subtitle = $"{lines.Count} lines · SO {so?.Header.SoNumber ?? "—"} · {h.Status}",
            IconClass = "ti-package",
            IconBgColor = "#EEEDFE",
            IconFgColor = "#534AB7",
            AvatarInitials = "",
            StatusLabel = h.Status,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Pack Tasks",
            BreadcrumbParentUrl = "/PackTasks",
            Stats = new()
            {
                new("Lines",   lines.Count.ToString("N0")),
                new("Picked",  totalPicked.ToString("N2")),
                new("Packed",
                    isTerminal ? $"{totalPacked:N2} / {totalPicked:N2}" : "—",
                    packedColor),
                new("Status",  h.Status),
            },
            ShowImagesTab = false,
            CustomTabs = isPacked
                ? new()
                {
                    new("lines", "Lines", "ti-list-details",
                        "Detail/_PackTaskLinesPanel", lines.Count),
                    // Phase 17 — Video tab on Packed only. Tab count
                    // shows 1 if a video exists, 0 if not (renders
                    // a "Record now" prompt in the empty state).
                    new("video", "Video", "ti-video",
                        "Detail/_PackTaskVideoPanel", latestVideo is not null ? 1 : 0),
                }
                : new()
                {
                    new("lines", "Lines", "ti-list-details",
                        "Detail/_PackTaskLinesPanel", lines.Count),
                },
            QuickActions = new()
            {
                new("Cancel", "ti-x",
                    canCancel ? "#cancel-pack-modal" : "#",
                    Enabled: canCancel),
            },
            OverviewFields = BuildOverviewFields(h, so?.Header.SoNumber, carton, boxTypes),
            Properties = BuildProperties(h),
        };

        ViewBag.HeaderId        = h.Id;
        ViewBag.HeaderStatus    = h.Status;
        ViewBag.IsPending       = isPending;
        ViewBag.IsPacked        = isPacked;
        ViewBag.IsCancelled     = isCancelled;
        ViewBag.IsTerminal      = isTerminal;
        ViewBag.CanSubmit       = canSubmit;
        ViewBag.CanCancel       = canCancel;
        ViewBag.LineRows        = lines;
        ViewBag.Carton          = carton;
        ViewBag.BoxTypes        = boxTypes;
        ViewBag.LatestVideo     = latestVideo;
        ViewBag.PackTaskMessage = TempData["PackTaskMessage"] as string;
        ViewBag.PackTaskError   = TempData["PackTaskError"]   as string;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    [HttpPost("Submit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid id, SubmitPackTaskViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var entries = vm.Lines
                .Select(l => new PackedLineEntry(
                    LineId: l.LineId,
                    PackedQuantity: l.PackedQuantity,
                    LineStatus: l.LineStatus,
                    ShortPackReason: string.IsNullOrWhiteSpace(l.ShortPackReason)
                        ? null : l.ShortPackReason.Trim(),
                    Notes: string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                .ToList();

            var request = new SubmitPackTaskRequest(
                PackTaskId: id,
                Lines: entries,
                BoxTypeId: vm.BoxTypeId == Guid.Empty ? null : vm.BoxTypeId,
                WeightKg: vm.WeightKg,
                CartonNotes: vm.CartonNotes);

            var result = await _service.SubmitAsync(tenantId, request, requesterId, ct);

            TempData["PackTaskMessage"] =
                $"Pack task submitted — carton {result.CartonNumber} ({result.FullyPackedLineCount} full, {result.ShortPackedLineCount} short, {result.SkippedLineCount} skipped, {result.TotalPackedQuantity:N2} packed). SO is now {result.SalesOrderStatus}.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["PackTaskError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid id, CancelPackTaskViewModel vm, CancellationToken ct)
    {
        vm = vm with { Id = id };

        var fv = await _cancelValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            TempData["PackTaskError"] = fv.Errors.FirstOrDefault()?.ErrorMessage
                ?? "Validation failed.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.CancelAsync(
                tenantId, id, vm.Reason.Trim(), requesterId, ct);
            TempData["PackTaskMessage"] = changed
                ? "Pack task cancelled — SO state unchanged (still Picked or PartiallyPicked)."
                : "Pack task was already cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["PackTaskError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    // ================================================================
    // Phase 17 (ADR-009) — Pack video endpoints
    // ================================================================

    // POST /PackTasks/UploadVideo/{id} — multipart blob upload.
    // Pack task must be Packed (operator records video AFTER sealing
    // the carton — see ADR-009 alternatives section). Returns JSON
    // {videoId} on success so the client can update the player UI
    // without a full page reload.
    [HttpPost("UploadVideo/{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60 * 1024 * 1024)]    // 60 MB hard cap (storage validates 50 MB but allow some HTTP framing)
    public async Task<IActionResult> UploadVideo(
        Guid id,
        IFormFile file,
        [FromForm] int durationSec,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No video file in request." });

        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            await using var stream = file.OpenReadStream();
            var videoId = await _videoService.UploadAsync(
                _tenant.RequireTenantId(),
                packTaskId: id,
                content: stream,
                fileName: file.FileName,
                contentType: file.ContentType,
                durationSec: durationSec,
                currentUserId: requesterId,
                ct);
            return Json(new { videoId });
        }
        catch (StorageValidationException ex)
        {
            // Size / extension rejection — surfaced as 400 so the
            // client shows a friendly message instead of a 500.
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET /PackTasks/Video/{videoId} — playback. Returns the raw
    // bytes with the right Content-Type. Range-request streaming is
    // a TD (current implementation reads the whole file into the
    // response stream — fine for 30-MB videos, less great for hours-
    // of-video futures).
    [HttpGet("Video/{videoId:guid}")]
    public async Task<IActionResult> Video(Guid videoId, CancellationToken ct)
    {
        var streamResult = await _videoService.GetStreamAsync(
            _tenant.RequireTenantId(), videoId, ct);
        if (streamResult is null) return NotFound();

        var (stream, contentType, fileName) = streamResult.Value;
        // enableRangeProcessing: true lets the framework handle
        // simple Range requests over the FileStream — better than
        // nothing while the proper streaming TD lands.
        return File(stream, contentType, fileName, enableRangeProcessing: true);
    }

    // DELETE /PackTasks/Video/{videoId} — admin/debug. Removes the
    // PackVideo row + the underlying documents.Files row + on-disk
    // bytes (mirrors the retention job's per-row delete logic).
    [HttpDelete("Video/{videoId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVideo(Guid videoId, CancellationToken ct)
    {
        var changed = await _videoService.DeleteAsync(
            _tenant.RequireTenantId(), videoId, ct);
        return changed ? NoContent() : NotFound();
    }

    private static List<KeyValuePair<string, string>> BuildOverviewFields(
        Domain.Entities.Outbound.PackTask h,
        string? soNumber,
        Domain.Entities.Outbound.Carton? carton,
        IReadOnlyList<LookupItem> boxTypes)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Pack #",     $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(h.PackNumber)}</span>"),
            new("Status",     System.Net.WebUtility.HtmlEncode(h.Status)),
            new("Sales order", soNumber is null
                ? "—"
                : $"<a href=\"/SalesOrders/Detail/{h.SalesOrderId}\" class=\"mono\">{System.Net.WebUtility.HtmlEncode(soNumber)}</a>"),
        };

        if (carton is not null)
        {
            var boxCode = carton.BoxTypeId.HasValue
                ? boxTypes.FirstOrDefault(b => b.Id == carton.BoxTypeId.Value)?.Code ?? "—"
                : "—";
            fields.Add(new("Carton #",
                $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(carton.CartonNumber)}</span>"));
            fields.Add(new("Box type",
                $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(boxCode)}</span>"));
            fields.Add(new("Weight",
                carton.WeightKg.HasValue ? $"{carton.WeightKg.Value:N3} kg" : "—"));
            if (!string.IsNullOrWhiteSpace(carton.Notes))
                fields.Add(new("Carton notes", System.Net.WebUtility.HtmlEncode(carton.Notes)));
        }

        fields.Add(new("Notes", System.Net.WebUtility.HtmlEncode(h.Notes ?? "—")));

        if (h.Status == "Cancelled")
        {
            fields.Add(new("Cancel reason",
                System.Net.WebUtility.HtmlEncode(h.CancelReason ?? "—")));
        }

        return fields;
    }

    private static List<KeyValuePair<string, string>> BuildProperties(
        Domain.Entities.Outbound.PackTask h)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Generated", RelativeTime.Format(h.GeneratedAt)),
        };

        if (h.PackedAt.HasValue)
            props.Add(new("Packed", RelativeTime.Format(h.PackedAt.Value)));
        if (h.CancelledAt.HasValue)
            props.Add(new("Cancelled", RelativeTime.Format(h.CancelledAt.Value)));

        return props;
    }
}
