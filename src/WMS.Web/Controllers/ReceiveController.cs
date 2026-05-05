using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Inbound;
using WMS.Common.Multitenancy;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Controllers;

// Single-screen receive form — Receiving-ε. Resolves natural-key
// codes (Product / Owner / Location / PO / PoLine) inline via Dapper
// against the tenant DB so the form keeps a "what's on the paperwork"
// shape and Phase 1 doesn't need new repo lookup methods. The actual
// post is done by IReceivingHeaderService.PostReceivingAsync, which
// orchestrates the four-step δ flow.
//
// [Authorize] only for now — RequirePermission gating comes when the
// inbound function code rules are firmed up.
[Authorize]
[Route("receive")]
public sealed class ReceiveController : BaseController
{
    private readonly IReceivingHeaderService _receivingHeaderService;
    private readonly ITenantConnectionFactory _tenantConn;

    public ReceiveController(
        IReceivingHeaderService receivingHeaderService,
        ITenantConnectionFactory tenantConn)
    {
        _receivingHeaderService = receivingHeaderService;
        _tenantConn = tenantConn;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        if (CurrentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        return View(new ReceiveFormModel { OwnerCode = "SELF", Quantity = 1 });
    }

    [HttpPost("post")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(ReceiveFormModel model, CancellationToken ct)
    {
        if (CurrentUser.WarehouseId is not { } warehouseId)
            return RedirectToAction("SelectWarehouse", "Auth");

        if (!ModelState.IsValid)
            return View(nameof(Index), model);

        // PO number / line either both or neither.
        if (!string.IsNullOrWhiteSpace(model.PoNumber) && model.PoLineNumber is null)
        {
            ModelState.AddModelError(nameof(model.PoLineNumber),
                "PO Line is required when PO Number is given.");
            return View(nameof(Index), model);
        }
        if (string.IsNullOrWhiteSpace(model.PoNumber) && model.PoLineNumber is not null)
        {
            ModelState.AddModelError(nameof(model.PoNumber),
                "PO Number is required when PO Line is given.");
            return View(nameof(Index), model);
        }

        var tenantId = TenantContext.RequireTenantId();
        using var conn = _tenantConn.CreateConnection(tenantId);

        // Resolve master-data codes → Ids. Each lookup is a tiny
        // SELECT; failures are reported as field-level model errors
        // so the operator sees exactly which code mistyped.
        var (productId, baseUomId) =
            await ResolveProductAsync(conn, model.ProductCode.Trim(), ct);
        if (productId is null)
            ModelState.AddModelError(nameof(model.ProductCode),
                $"Product '{model.ProductCode}' not found or inactive.");

        var ownerId = await ResolveOwnerAsync(conn, model.OwnerCode.Trim(), ct);
        if (ownerId is null)
            ModelState.AddModelError(nameof(model.OwnerCode),
                $"Owner '{model.OwnerCode}' not found or inactive.");

        var locationId = await ResolveLocationAsync(
            conn, warehouseId, model.LocationCode.Trim(), ct);
        if (locationId is null)
            ModelState.AddModelError(nameof(model.LocationCode),
                $"Location '{model.LocationCode}' not found in this warehouse.");

        Guid? poId = null;
        Guid? poLineId = null;
        if (!string.IsNullOrWhiteSpace(model.PoNumber))
        {
            poId = await ResolvePoAsync(conn, model.PoNumber.Trim(), ct);
            if (poId is null)
                ModelState.AddModelError(nameof(model.PoNumber),
                    $"PO '{model.PoNumber}' not found.");
            else if (model.PoLineNumber is { } poLineNumber)
            {
                poLineId = await ResolvePoLineAsync(conn, poId.Value, poLineNumber, ct);
                if (poLineId is null)
                    ModelState.AddModelError(nameof(model.PoLineNumber),
                        $"PO line {poLineNumber} not found on '{model.PoNumber}'.");
            }
        }

        if (!ModelState.IsValid)
            return View(nameof(Index), model);

        // Build the request — single-line receive at this point. Future
        // ε iterations may collect several lines on one receipt.
        var lotInfo = string.IsNullOrWhiteSpace(model.LotNumber)
            ? null
            : new LotInfo(
                model.LotNumber.Trim(),
                DateOnly.FromDateTime(DateTime.UtcNow),
                model.ExpiryDate);

        var palletInfo = string.IsNullOrWhiteSpace(model.PalletNumber)
            ? null
            : new PalletInfo(model.PalletNumber.Trim());

        var request = new PostReceivingRequest(
            ReceivingNumber: model.ReceivingNumber.Trim(),
            PurchaseOrderId: poId,
            WarehouseId: warehouseId,
            ReceivedAt: null,
            Notes: model.Notes,
            Lines: new[]
            {
                new PostReceivingLineRequest(
                    LineNumber: 1,
                    PurchaseOrderLineId: poLineId,
                    ProductId: productId!.Value,
                    UomId: baseUomId!.Value,
                    OwnerId: ownerId!.Value,
                    LocationId: locationId!.Value,
                    ReceivedQuantity: model.Quantity,
                    Lot: lotInfo,
                    Pallet: palletInfo),
            });

        try
        {
            var detail = await _receivingHeaderService.PostReceivingAsync(
                tenantId, request, CurrentUser.UserId, ct);
            return RedirectToAction(nameof(Posted), new { id = detail.Header.Id });
        }
        catch (Exception ex)
        {
            // Catches DB-level violations (duplicate ReceivingNumber,
            // FK orphans surfacing late). User sees the message inline.
            ModelState.AddModelError(string.Empty, ex.Message);
            Logger.LogWarning(ex, "Receive failed for {ReceivingNumber}", model.ReceivingNumber);
            return View(nameof(Index), model);
        }
    }

    [HttpGet("posted/{id:guid}")]
    public async Task<IActionResult> Posted(Guid id, CancellationToken ct)
    {
        var detail = await _receivingHeaderService.GetByIdAsync(
            TenantContext.RequireTenantId(), id, ct);
        if (detail is null) return NotFound();
        return View(detail);
    }

    // --- code resolvers ----------------------------------------------

    private static async Task<(Guid? ProductId, Guid? BaseUomId)> ResolveProductAsync(
        System.Data.IDbConnection conn, string code, CancellationToken ct)
    {
        var row = await conn.QuerySingleOrDefaultAsync<(Guid Id, Guid BaseUomId)?>(
            new CommandDefinition(
                "SELECT Id, BaseUomId FROM master.Products " +
                "WHERE Code = @code AND Status = 'Active'",
                new { code },
                cancellationToken: ct));
        return row is null ? (null, null) : (row.Value.Id, row.Value.BaseUomId);
    }

    private static Task<Guid?> ResolveOwnerAsync(
        System.Data.IDbConnection conn, string code, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT Id FROM master.Owners WHERE Code = @code AND IsActive = 1",
            new { code },
            cancellationToken: ct));

    private static Task<Guid?> ResolveLocationAsync(
        System.Data.IDbConnection conn, Guid warehouseId, string code, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT Id FROM master.Locations " +
            "WHERE WarehouseId = @warehouseId AND Code = @code " +
            "  AND IsActive = 1 AND Status = 'Active'",
            new { warehouseId, code },
            cancellationToken: ct));

    private static Task<Guid?> ResolvePoAsync(
        System.Data.IDbConnection conn, string poNumber, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT Id FROM inbound.PurchaseOrders " +
            "WHERE PoNumber = @poNumber AND Status IN ('Open', 'Receiving')",
            new { poNumber },
            cancellationToken: ct));

    private static Task<Guid?> ResolvePoLineAsync(
        System.Data.IDbConnection conn, Guid poId, int lineNumber, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT Id FROM inbound.PurchaseOrderLines " +
            "WHERE PurchaseOrderId = @poId AND LineNumber = @lineNumber",
            new { poId, lineNumber },
            cancellationToken: ct));
}
