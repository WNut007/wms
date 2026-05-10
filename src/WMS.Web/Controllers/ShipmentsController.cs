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

// Phase 14E — Shipment execution surface.
//   GET  /Shipments                — Phase 15A list page (chip counts + table + pagination)
//   GET  /Shipments/Data           — Phase 15A JSON envelope for Alpine
//   GET  /Shipments/Detail/{id}    — _DetailLayout w/ inline submit form (Pending) or read-only summary (terminal)
//   POST /Shipments/Submit/{id}    — TX-wrapped commit via ShipmentService.SubmitAsync
//   POST /Shipments/Cancel/{id}    — pre-Submit reversal via ShipmentService.CancelAsync
[Authorize]
[Route("Shipments")]
public sealed class ShipmentsController : Controller
{
    private readonly IShipmentRepositoryFactory _shipmentRepos;
    private readonly ICartonRepositoryFactory _cartonRepos;
    private readonly ISalesOrderRepositoryFactory _soRepos;
    private readonly IShipmentService _service;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<CancelShipmentViewModel> _cancelValidator;

    public ShipmentsController(
        IShipmentRepositoryFactory shipmentRepos,
        ICartonRepositoryFactory cartonRepos,
        ISalesOrderRepositoryFactory soRepos,
        IShipmentService service,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<CancelShipmentViewModel> cancelValidator)
    {
        _shipmentRepos = shipmentRepos;
        _cartonRepos = cartonRepos;
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
        var filter = new ShipmentFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: ShipmentStatusMapper.FromWire(status),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _shipmentRepos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(r => new
            {
                id              = r.Id,
                shipmentNumber  = r.ShipmentNumber,
                salesOrderId    = r.SalesOrderId,
                soNumber        = r.SoNumber,
                customerCode    = r.CustomerCode,
                customerName    = r.CustomerName,
                status          = ShipmentStatusMapper.ToWire(r.Status),
                statusLabel     = r.Status,
                carrierName     = r.CarrierName,
                trackingNumber  = r.TrackingNumber,
                cartonCount     = r.CartonCount,
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
                shipped   = counts.Shipped,
                cancelled = counts.Cancelled,
            },
        });
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var shipment = await _shipmentRepos.For(tenantId).GetByIdAsync(id, ct);
        if (shipment is null) return NotFound();

        var so = await _soRepos.For(tenantId).GetByIdAsync(shipment.SalesOrderId, ct);

        // Cartons are claimed at SubmitAsync time — the read returns
        // empty for Pending shipments, populated for Shipped, possibly
        // stale (carton may have been re-stamped to a future shipment
        // if cancellation logic ever expands) for Cancelled.
        var cartons = await _cartonRepos.For(tenantId).GetByShipmentIdAsync(id, ct);

        var isPending   = shipment.Status == "Pending";
        var isShipped   = shipment.Status == "Shipped";
        var isCancelled = shipment.Status == "Cancelled";
        var isTerminal  = isShipped || isCancelled;

        var canSubmit = isPending;
        var canCancel = isPending;

        var statusVariant = ShipmentStatusMapper.ToBadgeVariant(shipment.Status);

        var totalWeight = cartons.Sum(c => c.WeightKg ?? 0m);

        var vm = new DetailPageViewModel
        {
            EntityType = "Shipment",
            EntityId = id.ToString(),
            Title = shipment.ShipmentNumber,
            Subtitle = $"SO {so?.Header.SoNumber ?? "—"} · {cartons.Count} carton(s) · {shipment.Status}",
            IconClass = "ti-truck-delivery",
            IconBgColor = "#EEEDFE",
            IconFgColor = "#534AB7",
            AvatarInitials = "",
            StatusLabel = shipment.Status,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Shipments",
            BreadcrumbParentUrl = "/Shipments",
            Stats = new()
            {
                new("Cartons",  cartons.Count.ToString("N0")),
                new("Weight",   totalWeight > 0m ? $"{totalWeight:N3} kg" : "—"),
                new("Carrier",  shipment.CarrierName ?? "—"),
                new("Status",   shipment.Status),
            },
            ShowImagesTab = false,
            CustomTabs = new()
            {
                new("dispatch", "Dispatch", "ti-truck-delivery",
                    "Detail/_ShipmentDispatchPanel", isTerminal ? cartons.Count : null),
            },
            QuickActions = new()
            {
                new("Cancel", "ti-x",
                    canCancel ? "#cancel-ship-modal" : "#",
                    Enabled: canCancel),
            },
            OverviewFields = BuildOverviewFields(shipment, so?.Header.SoNumber),
            Properties = BuildProperties(shipment),
        };

        ViewBag.HeaderId        = shipment.Id;
        ViewBag.HeaderStatus    = shipment.Status;
        ViewBag.IsPending       = isPending;
        ViewBag.IsShipped       = isShipped;
        ViewBag.IsCancelled     = isCancelled;
        ViewBag.IsTerminal      = isTerminal;
        ViewBag.CanSubmit       = canSubmit;
        ViewBag.CanCancel       = canCancel;
        ViewBag.Shipment        = shipment;
        ViewBag.Cartons         = cartons;
        ViewBag.ShipmentMessage = TempData["ShipmentMessage"] as string;
        ViewBag.ShipmentError   = TempData["ShipmentError"]   as string;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    [HttpPost("Submit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid id, SubmitShipmentViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var request = new SubmitShipmentRequest(
                ShipmentId: id,
                CarrierName: vm.CarrierName,
                TrackingNumber: vm.TrackingNumber,
                Notes: vm.Notes);

            var result = await _service.SubmitAsync(tenantId, request, requesterId, ct);

            TempData["ShipmentMessage"] =
                $"Shipment dispatched — {result.CartonCount} carton(s) stamped. SO is now {result.SalesOrderStatus}.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["ShipmentError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid id, CancelShipmentViewModel vm, CancellationToken ct)
    {
        vm = vm with { Id = id };

        var fv = await _cancelValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            TempData["ShipmentError"] = fv.Errors.FirstOrDefault()?.ErrorMessage
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
            TempData["ShipmentMessage"] = changed
                ? "Shipment cancelled — SO state unchanged (still Packed)."
                : "Shipment was already cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ShipmentError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    private static List<KeyValuePair<string, string>> BuildOverviewFields(
        Domain.Entities.Outbound.Shipment s, string? soNumber)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Shipment #",  $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(s.ShipmentNumber)}</span>"),
            new("Status",      System.Net.WebUtility.HtmlEncode(s.Status)),
            new("Sales order", soNumber is null
                ? "—"
                : $"<a href=\"/SalesOrders/Detail/{s.SalesOrderId}\" class=\"mono\">{System.Net.WebUtility.HtmlEncode(soNumber)}</a>"),
            new("Carrier",     System.Net.WebUtility.HtmlEncode(s.CarrierName ?? "—")),
            new("Tracking #",  s.TrackingNumber is null
                ? "—"
                : $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(s.TrackingNumber)}</span>"),
            new("Notes",       System.Net.WebUtility.HtmlEncode(s.Notes ?? "—")),
        };

        if (s.Status == "Cancelled")
        {
            fields.Add(new("Cancel reason",
                System.Net.WebUtility.HtmlEncode(s.CancelReason ?? "—")));
        }

        return fields;
    }

    private static List<KeyValuePair<string, string>> BuildProperties(
        Domain.Entities.Outbound.Shipment s)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Generated", RelativeTime.Format(s.GeneratedAt)),
        };

        if (s.ShippedAt.HasValue)
            props.Add(new("Shipped", RelativeTime.Format(s.ShippedAt.Value)));
        if (s.CancelledAt.HasValue)
            props.Add(new("Cancelled", RelativeTime.Format(s.CancelledAt.Value)));

        return props;
    }
}
