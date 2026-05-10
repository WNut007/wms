using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Outbound;
using WMS.Web.Models.Outbound;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

// Phase 14C — Pick task execution surface.
//   GET  /PickTasks                — Phase 15A list page (chip counts + table + pagination)
//   GET  /PickTasks/Data           — Phase 15A JSON envelope for Alpine
//   GET  /PickTasks/Detail/{id}    — _DetailLayout w/ inline submit form (Pending|InProgress) or read-only table (terminal)
//   POST /PickTasks/Submit/{id}    — TX-wrapped commit via PickTaskService.SubmitAsync
//   POST /PickTasks/Cancel/{id}    — pre-Submit reversal via PickTaskService.CancelAsync
[Authorize]
[Route("PickTasks")]
public sealed class PickTasksController : Controller
{
    private readonly IPickTaskRepositoryFactory _pickRepos;
    private readonly ISalesOrderRepositoryFactory _soRepos;
    private readonly IPickTaskService _service;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<CancelPickTaskViewModel> _cancelValidator;

    public PickTasksController(
        IPickTaskRepositoryFactory pickRepos,
        ISalesOrderRepositoryFactory soRepos,
        IPickTaskService service,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<CancelPickTaskViewModel> cancelValidator)
    {
        _pickRepos = pickRepos;
        _soRepos = soRepos;
        _service = service;
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
        var filter = new PickTaskFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: PickTaskStatusMapper.FromWire(status),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _pickRepos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(r => new
            {
                id              = r.Id,
                pickNumber      = r.PickNumber,
                salesOrderId    = r.SalesOrderId,
                soNumber        = r.SoNumber,
                customerCode    = r.CustomerCode,
                customerName    = r.CustomerName,
                status          = PickTaskStatusMapper.ToWire(r.Status),
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
                all             = counts.All,
                pending         = counts.Pending,
                inprogress      = counts.InProgress,
                picked          = counts.Picked,
                partiallypicked = counts.PartiallyPicked,
                cancelled       = counts.Cancelled,
            },
        });
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var detail = await _pickRepos.For(tenantId).GetByIdAsync(id, ct);
        if (detail is null) return NotFound();

        var h = detail.Header;
        var lines = detail.Lines;

        // SO header for the Overview block (SoNumber + Customer name).
        var so = await _soRepos.For(tenantId).GetByIdAsync(h.SalesOrderId, ct);

        var isPending     = h.Status == "Pending";
        var isInProgress  = h.Status == "InProgress";
        var isPicked      = h.Status == "Picked";
        var isPartial     = h.Status == "PartiallyPicked";
        var isCancelled   = h.Status == "Cancelled";
        var isTerminal    = isPicked || isPartial || isCancelled;

        var canSubmit = isPending || isInProgress;
        var canCancel = isPending || isInProgress;

        var statusVariant = PickTaskStatusMapper.ToBadgeVariant(h.Status);

        var totalExpected = lines.Sum(l => l.ExpectedQuantity);
        var totalPicked   = lines.Sum(l => l.PickedQuantity ?? 0m);
        var pickedLines   = lines.Count(l => l.LineStatus == "Picked");
        var skippedLines  = lines.Count(l => l.LineStatus == "Skipped");

        // "Picked" stat tile colour: green when fully picked, amber on
        // any short, neutral pre-submit.
        string? pickedColor = null;
        if (isPicked) pickedColor = "#0F6E56";
        else if (isPartial) pickedColor = "#854F0B";

        var vm = new DetailPageViewModel
        {
            EntityType = "PickTask",
            EntityId = id.ToString(),
            Title = h.PickNumber,
            Subtitle = $"{lines.Count} lines · SO {so?.Header.SoNumber ?? "—"} · {h.Status}",
            IconClass = "ti-list-check",
            IconBgColor = "#EEEDFE",
            IconFgColor = "#534AB7",
            AvatarInitials = "",
            StatusLabel = h.Status,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Pick Tasks",
            BreadcrumbParentUrl = "/PickTasks",
            Stats = new()
            {
                new("Lines",    lines.Count.ToString("N0")),
                new("Expected", totalExpected.ToString("N2")),
                new("Picked",
                    isTerminal ? $"{totalPicked:N2} / {totalExpected:N2}" : "—",
                    pickedColor),
                new("Status",   h.Status),
            },
            ShowImagesTab = false,
            CustomTabs = new()
            {
                // Lines tab — submit form when Pending|InProgress;
                // read-only with picked/expected/short highlight on
                // Picked / PartiallyPicked / Cancelled.
                new("lines", "Lines", "ti-list-details",
                    "Detail/_PickTaskLinesPanel", lines.Count),
            },
            QuickActions = new()
            {
                new("Cancel", "ti-x",
                    canCancel ? "#cancel-pick-modal" : "#",
                    Enabled: canCancel),
            },
            OverviewFields = BuildOverviewFields(h, so?.Header.SoNumber),
            Properties = BuildProperties(h),
        };

        ViewBag.HeaderId          = h.Id;
        ViewBag.HeaderStatus      = h.Status;
        ViewBag.IsPending         = isPending;
        ViewBag.IsInProgress      = isInProgress;
        ViewBag.IsPicked          = isPicked;
        ViewBag.IsPartiallyPicked = isPartial;
        ViewBag.IsCancelled       = isCancelled;
        ViewBag.IsTerminal        = isTerminal;
        ViewBag.CanSubmit         = canSubmit;
        ViewBag.CanCancel         = canCancel;
        ViewBag.LineRows          = lines;
        ViewBag.PickTaskMessage   = TempData["PickTaskMessage"] as string;
        ViewBag.PickTaskError     = TempData["PickTaskError"]   as string;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    [HttpPost("Submit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid id, SubmitPickTaskViewModel vm, CancellationToken ct)
    {
        // Route id wins over the form-bound Id (defence against tampered
        // forms; matches Phase 10B precedent).
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
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

            var result = await _service.SubmitAsync(tenantId, request, requesterId, ct);

            TempData["PickTaskMessage"] = result.TaskStatus == "Picked"
                ? $"Pick task submitted — full pick complete ({result.FullyPickedLineCount} lines, {result.TotalPickedQuantity:N2} picked). SO is now {result.SalesOrderStatus}."
                : $"Pick task submitted — partial pick ({result.FullyPickedLineCount} full, {result.ShortPickedLineCount} short, {result.SkippedLineCount} skipped). SO is now {result.SalesOrderStatus}.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["PickTaskError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid id, CancelPickTaskViewModel vm, CancellationToken ct)
    {
        vm = vm with { Id = id };

        var fv = await _cancelValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            TempData["PickTaskError"] = fv.Errors.FirstOrDefault()?.ErrorMessage
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
            TempData["PickTaskMessage"] = changed
                ? "Pick task cancelled — SO returned to Allocated."
                : "Pick task was already cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["PickTaskError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    private static List<KeyValuePair<string, string>> BuildOverviewFields(
        Domain.Entities.Outbound.PickTask h, string? soNumber)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Pick #",     $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(h.PickNumber)}</span>"),
            new("Status",     System.Net.WebUtility.HtmlEncode(h.Status)),
            new("Sales order", soNumber is null
                ? "—"
                : $"<a href=\"/SalesOrders/Detail/{h.SalesOrderId}\" class=\"mono\">{System.Net.WebUtility.HtmlEncode(soNumber)}</a>"),
            new("Notes",      System.Net.WebUtility.HtmlEncode(h.Notes ?? "—")),
        };

        if (h.Status == "Cancelled")
        {
            fields.Add(new("Cancel reason",
                System.Net.WebUtility.HtmlEncode(h.CancelReason ?? "—")));
        }

        return fields;
    }

    private static List<KeyValuePair<string, string>> BuildProperties(
        Domain.Entities.Outbound.PickTask h)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Generated", RelativeTime.Format(h.GeneratedAt)),
        };

        if (h.StartedAt.HasValue)
            props.Add(new("Started", RelativeTime.Format(h.StartedAt.Value)));
        if (h.CompletedAt.HasValue)
            props.Add(new("Completed", RelativeTime.Format(h.CompletedAt.Value)));
        if (h.CancelledAt.HasValue)
            props.Add(new("Cancelled", RelativeTime.Format(h.CancelledAt.Value)));

        return props;
    }
}
