using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Counts;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Counts;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Counts;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

// Phase 12 (counts.* domain) — Cycle Count workflow.
//   GET  /CycleCounts                       — Alpine list with chip counts
//   GET  /CycleCounts/Data                  — JSON envelope
//   GET  /CycleCounts/Create                — pick warehouse + optional location
//   POST /CycleCounts/Create                — snapshot + redirect to Detail
//   GET  /CycleCounts/Detail/{id}           — header + lines (inline count form when Counting)
//   POST /CycleCounts/SaveCounts/{id}       — bulk save line counts (Counting only)
//   POST /CycleCounts/Submit/{id}           — Counting → Review
//   POST /CycleCounts/Apply/{id}            — Review → Applied (TX-wrapped)
//   POST /CycleCounts/Cancel/{id}           — Counting/Review → Cancelled
//   GET  /CycleCounts/Locations/{whId}      — JSON helper (shared shape with /Adjustments)
[Authorize]
[Route("CycleCounts")]
public sealed class CycleCountsController : Controller
{
    private readonly ICycleCountRepositoryFactory _repos;
    private readonly ICycleCountService _service;
    private readonly IWarehouseRepositoryFactory _warehouseRepos;
    private readonly ILocationRepositoryFactory _locationRepos;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<CycleCountCreateViewModel> _createValidator;
    private readonly IValidator<CancelCycleCountViewModel> _cancelValidator;

    public CycleCountsController(
        ICycleCountRepositoryFactory repos,
        ICycleCountService service,
        IWarehouseRepositoryFactory warehouseRepos,
        ILocationRepositoryFactory locationRepos,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<CycleCountCreateViewModel> createValidator,
        IValidator<CancelCycleCountViewModel> cancelValidator)
    {
        _repos = repos;
        _service = service;
        _warehouseRepos = warehouseRepos;
        _locationRepos = locationRepos;
        _tenant = tenant;
        _currentUser = currentUser;
        _createValidator = createValidator;
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
        string? warehouse = null,
        string sortBy = "startedAt",
        bool sortDesc = true,
        CancellationToken ct = default)
    {
        var filter = new CycleCountFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: CycleCountStatusMapper.FromWire(status),
            WarehouseCode: NormaliseFilter(warehouse),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(r => new
            {
                id                 = r.Id,
                countNumber        = r.CountNumber,
                warehouseCode      = r.WarehouseCode,
                locationFilterCode = r.LocationFilterCode ?? "(whole warehouse)",
                status             = CycleCountStatusMapper.ToWire(r.Status),
                statusLabel        = r.Status,
                lineCount          = r.LineCount,
                countedLineCount   = r.CountedLineCount,
                varianceLineCount  = r.VarianceLineCount,
                startedByName      = r.StartedByName,
                startedAt          = r.StartedAt,
                startedRelative    = RelativeTime.Format(r.StartedAt),
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all       = counts.All,
                counting  = counts.Counting,
                review    = counts.Review,
                applied   = counts.Applied,
                cancelled = counts.Cancelled,
            },
        });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new CycleCountCreateViewModel
        {
            WarehouseId = _currentUser.WarehouseId ?? Guid.Empty,
        };
        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CycleCountCreateViewModel vm, CancellationToken ct)
    {
        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }

        try
        {
            var requesterId = _currentUser.UserId
                ?? throw new InvalidOperationException("Authenticated user required.");

            var request = new CreateCycleCountRequest(
                WarehouseId: vm.WarehouseId,
                LocationFilter: vm.LocationFilter,
                Notes: string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim());

            var saved = await _service.CreateAsync(
                _tenant.RequireTenantId(), request, requesterId, ct);

            TempData["CycleCountMessage"] =
                $"Cycle count {saved.Header.CountNumber} created — {saved.Lines.Count} lines snapshot.";
            return RedirectToAction(nameof(Detail), new { id = saved.Header.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var detail = await _service.GetByIdAsync(tenantId, id, ct);
        if (detail is null) return NotFound();

        var lineRows = await _repos.For(tenantId).GetLineRowsByIdAsync(id, ct);
        var h = detail.Header;

        var currentUserId = _currentUser.UserId;

        // State-driven Quick Action gating:
        //  * Submit (Counting → Review)   — visible during Counting
        //  * Apply (Review → Applied)     — Review state + currentUser != counter
        //  * Cancel                       — Counting OR Review
        var isCounting = h.Status == "Counting";
        var isReview   = h.Status == "Review";
        var canApply   = isReview
            && currentUserId is not null
            && currentUserId.Value != h.CountedBy;
        var canCancel  = isCounting || isReview;

        var statusVariant = CycleCountStatusMapper.ToBadgeVariant(h.Status);

        var countedSoFar = lineRows.Count(l => l.LineStatus == "Counted");
        var withVariance = lineRows.Count(l =>
            l.LineStatus == "Counted" && l.CountedQuantity != l.ExpectedQuantity);

        var vm = new DetailPageViewModel
        {
            EntityType = "CycleCount",
            EntityId = id.ToString(),
            Title = h.CountNumber,
            Subtitle = $"{lineRows.Count} lines · {countedSoFar} counted · {withVariance} with variance",
            IconClass = "ti-list-check",
            IconBgColor = "#EEEDFE",
            IconFgColor = "#534AB7",
            AvatarInitials = "",
            StatusLabel = h.Status,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Cycle Counts",
            BreadcrumbParentUrl = "/CycleCounts",
            Stats = new()
            {
                new("Lines",          lineRows.Count.ToString("N0")),
                new("Counted",        countedSoFar.ToString("N0"),
                                       countedSoFar == lineRows.Count && lineRows.Count > 0 ? "#1D9E75" : null),
                new("With variance",  withVariance.ToString("N0"),
                                       withVariance > 0 ? "#854F0B" : null),
                new("Status",         h.Status),
            },
            ShowImagesTab = false,
            CustomTabs = new()
            {
                // Lines tab — count form when Counting; read-only with
                // variance highlight in Review/Applied/Cancelled.
                new("lines", "Lines", "ti-list-details",
                    "Detail/_CycleCountLinesPanel", lineRows.Count),
            },
            QuickActions = new()
            {
                new("Submit for review", "ti-check",
                    isCounting ? "#submit-modal" : "#",
                    Enabled: isCounting),
                new("Apply", "ti-circle-check",
                    canApply ? "#apply-modal" : "#",
                    Enabled: canApply),
                new("Cancel", "ti-x",
                    canCancel ? "#cancel-modal" : "#",
                    Enabled: canCancel),
            },
            OverviewFields = BuildOverviewFields(h),
            Properties = BuildProperties(h),
        };

        ViewBag.HeaderId        = h.Id;
        ViewBag.HeaderStatus    = h.Status;
        ViewBag.IsCounting      = isCounting;
        ViewBag.CanApply        = canApply;
        ViewBag.CanCancel       = canCancel;
        // Self-approval banner reuse: same flag name as Phase 11A so
        // _DetailLayout's existing check shows the message.
        ViewBag.SelfApproval    = isReview
            && currentUserId is not null
            && currentUserId.Value == h.CountedBy;
        ViewBag.CountedSoFar    = countedSoFar;
        ViewBag.LineRows        = lineRows;
        ViewBag.CountMessage    = TempData["CycleCountMessage"] as string;
        ViewBag.CountError      = TempData["CycleCountError"] as string;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    [HttpPost("SaveCounts/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCounts(
        Guid id, SaveCountsViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var updates = vm.Lines
                .Select(l => new CountLineUpdate(
                    LineId: l.LineId,
                    CountedQuantity: l.CountedQuantity,
                    LineStatus: l.LineStatus,
                    Notes: string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                .ToList();

            await _service.SaveCountedQuantitiesAsync(tenantId, id, updates, requesterId, ct);
            TempData["CycleCountMessage"] = $"Saved {updates.Count} line entries.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["CycleCountError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Submit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.SubmitForReviewAsync(tenantId, id, requesterId, ct);
            TempData["CycleCountMessage"] = changed
                ? "Cycle count submitted for review. Another user must approve."
                : "Cycle count was already in review.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["CycleCountError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Apply/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var approverId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.ApproveAndApplyAsync(tenantId, id, approverId, ct);
            TempData["CycleCountMessage"] = changed
                ? "Cycle count applied. Stock variances written to Movement Log."
                : "Cycle count was already applied.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["CycleCountError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid id, CancelCycleCountViewModel vm, CancellationToken ct)
    {
        vm = vm with { Id = id };

        var fv = await _cancelValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            TempData["CycleCountError"] = fv.Errors.FirstOrDefault()?.ErrorMessage
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
            TempData["CycleCountMessage"] = changed
                ? "Cycle count cancelled."
                : "Cycle count was already cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["CycleCountError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpGet("Locations/{warehouseId:guid}")]
    public async Task<IActionResult> Locations(Guid warehouseId, CancellationToken ct)
    {
        var rows = await _locationRepos.For(_tenant.RequireTenantId())
                                       .GetActiveByWarehouseAsync(warehouseId, ct);
        return Json(rows.Select(l => new { id = l.Id, code = l.Code, name = l.Name }));
    }

    private async Task PopulateLookupsAsync(
        CycleCountCreateViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();

        vm.Warehouses = (await _warehouseRepos.For(tenantId).GetActiveAsync(ct))
            .Select(w => new WMS.DAL.Common.LookupItem(w.Id, w.Code, w.Name))
            .ToList();

        if (vm.WarehouseId != Guid.Empty)
        {
            var rows = await _locationRepos.For(tenantId)
                                           .GetActiveByWarehouseAsync(vm.WarehouseId, ct);
            vm.Locations = rows.Select(l => new WMS.DAL.Common.LookupItem(l.Id, l.Code, l.Name)).ToList();
        }
    }

    private static List<KeyValuePair<string, string>> BuildOverviewFields(
        Domain.Entities.Counts.CycleCount h)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Count #",   $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(h.CountNumber)}</span>"),
            new("Status",    System.Net.WebUtility.HtmlEncode(h.Status)),
            new("Scope",     h.LocationFilter.HasValue
                ? $"Single location ({System.Net.WebUtility.HtmlEncode(h.LocationFilter.Value.ToString())})"
                : "Whole warehouse"),
            new("Notes",     System.Net.WebUtility.HtmlEncode(h.Notes ?? "—")),
        };

        if (h.Status == "Cancelled")
        {
            fields.Add(new("Cancel reason",
                System.Net.WebUtility.HtmlEncode(h.CancelReason ?? "—")));
        }

        return fields;
    }

    private static List<KeyValuePair<string, string>> BuildProperties(
        Domain.Entities.Counts.CycleCount h)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Started", RelativeTime.Format(h.StartedAt)),
        };

        if (h.CountedAt.HasValue)
            props.Add(new("Submitted", RelativeTime.Format(h.CountedAt.Value)));
        if (h.AppliedAt.HasValue)
            props.Add(new("Applied", RelativeTime.Format(h.AppliedAt.Value)));
        if (h.CancelledAt.HasValue)
            props.Add(new("Cancelled", RelativeTime.Format(h.CancelledAt.Value)));

        return props;
    }

    private static string? NormaliseFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null : value;
}
