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
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<CancelPackTaskViewModel> _cancelValidator;

    public PackTasksController(
        IPackTaskRepositoryFactory packRepos,
        ISalesOrderRepositoryFactory soRepos,
        IBoxTypeRepositoryFactory boxTypeRepos,
        IPackTaskService service,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<CancelPackTaskViewModel> cancelValidator)
    {
        _packRepos = packRepos;
        _soRepos = soRepos;
        _boxTypeRepos = boxTypeRepos;
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
            CustomTabs = new()
            {
                // Lines tab — submit form when Pending; read-only with
                // packed/picked/short highlight on Packed / Cancelled.
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
