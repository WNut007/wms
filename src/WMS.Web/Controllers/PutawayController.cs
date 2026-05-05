using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Inbound;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Controllers;

// Single-screen putaway form — Receiving-ζ. Resolves natural-key
// codes (Product / Owner / From-Loc / To-Loc + optional Lot / Pallet)
// inline via Dapper against the tenant DB, then hands a fully-built
// StockKey to IPutawayService.PutawayStockAsync.
//
// [Authorize] only for now; permission gating defers, same as
// ReceiveController.
[Authorize]
[Route("putaway")]
public sealed class PutawayController : BaseController
{
    private readonly IPutawayService _putawayService;
    private readonly ITenantConnectionFactory _tenantConn;

    public PutawayController(
        IPutawayService putawayService,
        ITenantConnectionFactory tenantConn)
    {
        _putawayService = putawayService;
        _tenantConn = tenantConn;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        if (CurrentUser.WarehouseId is null)
            return RedirectToAction("SelectWarehouse", "Auth");

        return View(new PutawayFormModel { OwnerCode = "SELF", Quantity = 1 });
    }

    [HttpPost("post")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(PutawayFormModel model, CancellationToken ct)
    {
        if (CurrentUser.WarehouseId is not { } warehouseId)
            return RedirectToAction("SelectWarehouse", "Auth");

        if (!ModelState.IsValid)
            return View(nameof(Index), model);

        var tenantId = TenantContext.RequireTenantId();
        using var conn = _tenantConn.CreateConnection(tenantId);

        // Resolve master-data codes — Product / Owner / both Locations.
        var (productId, _) =
            await ResolveProductAsync(conn, model.ProductCode.Trim(), ct);
        if (productId is null)
            ModelState.AddModelError(nameof(model.ProductCode),
                $"Product '{model.ProductCode}' not found or inactive.");

        var ownerId = await ResolveOwnerAsync(conn, model.OwnerCode.Trim(), ct);
        if (ownerId is null)
            ModelState.AddModelError(nameof(model.OwnerCode),
                $"Owner '{model.OwnerCode}' not found or inactive.");

        var fromLocationId = await ResolveLocationAsync(
            conn, warehouseId, model.FromLocationCode.Trim(), ct);
        if (fromLocationId is null)
            ModelState.AddModelError(nameof(model.FromLocationCode),
                $"Location '{model.FromLocationCode}' not found in this warehouse.");

        var toLocationId = await ResolveLocationAsync(
            conn, warehouseId, model.ToLocationCode.Trim(), ct);
        if (toLocationId is null)
            ModelState.AddModelError(nameof(model.ToLocationCode),
                $"Location '{model.ToLocationCode}' not found in this warehouse.");

        // Lot / Pallet are optional — but if entered, they must already
        // exist (putaway moves stock that's already on the books).
        Guid? lotId = null;
        if (productId is { } pid && !string.IsNullOrWhiteSpace(model.LotNumber))
        {
            lotId = await ResolveLotAsync(conn, pid, model.LotNumber.Trim(), ct);
            if (lotId is null)
                ModelState.AddModelError(nameof(model.LotNumber),
                    $"Lot '{model.LotNumber}' not found for product '{model.ProductCode}'.");
        }

        Guid? palletId = null;
        if (!string.IsNullOrWhiteSpace(model.PalletNumber))
        {
            palletId = await ResolvePalletAsync(conn, model.PalletNumber.Trim(), ct);
            if (palletId is null)
                ModelState.AddModelError(nameof(model.PalletNumber),
                    $"Pallet '{model.PalletNumber}' not found.");
        }

        // We need the BaseUomId for the StockKey. Re-read off the product
        // row — same Dapper conn, same tenant.
        Guid? uomId = null;
        if (productId is { } pid2)
            uomId = await ResolveProductBaseUomAsync(conn, pid2, ct);

        if (!ModelState.IsValid)
            return View(nameof(Index), model);

        var fromKey = new StockKey(
            LocationId: fromLocationId!.Value,
            ProductId: productId!.Value,
            LotId: lotId,
            PalletId: palletId,
            OwnerId: ownerId!.Value,
            UomId: uomId!.Value);

        try
        {
            var result = await _putawayService.PutawayStockAsync(
                tenantId,
                new PutawayRequest(fromKey, toLocationId!.Value, model.Quantity),
                CurrentUser.UserId,
                ct);

            return RedirectToAction(
                nameof(Posted),
                new { sourceId = result.Source.Id, destId = result.Destination.Id });
        }
        catch (Exception ex)
        {
            // Catches "no source row" + SqlException 50001 / 50002 / 50003
            // raised by the repo's batch.
            ModelState.AddModelError(string.Empty, ex.Message);
            Logger.LogWarning(ex, "Putaway failed for product {ProductCode}", model.ProductCode);
            return View(nameof(Index), model);
        }
    }

    [HttpGet("posted/{sourceId:guid}/{destId:guid}")]
    public async Task<IActionResult> Posted(Guid sourceId, Guid destId, CancellationToken ct)
    {
        var tenantId = TenantContext.RequireTenantId();
        using var conn = _tenantConn.CreateConnection(tenantId);

        // Two tiny Dapper queries — the entities are already known and
        // we just want fresh OnHand values to render. Avoids needing a
        // dedicated DTO.
        var rows = await conn.QueryAsync<PutawayPostedRow>(new CommandDefinition(
            @"SELECT s.Id, s.QuantityOnHand, l.Code AS LocationCode, p.Code AS ProductCode
              FROM inventory.Stock s
              JOIN master.Locations l ON l.Id = s.LocationId
              JOIN master.Products  p ON p.Id = s.ProductId
              WHERE s.Id IN (@sourceId, @destId);",
            new { sourceId, destId },
            cancellationToken: ct));

        var list = rows.ToList();
        var source = list.FirstOrDefault(r => r.Id == sourceId);
        var dest = list.FirstOrDefault(r => r.Id == destId);
        if (source is null || dest is null) return NotFound();

        return View(new PutawayPostedViewModel(source, dest));
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

    private static Task<Guid?> ResolveProductBaseUomAsync(
        System.Data.IDbConnection conn, Guid productId, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT BaseUomId FROM master.Products WHERE Id = @id",
            new { id = productId }, cancellationToken: ct));

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

    private static Task<Guid?> ResolveLotAsync(
        System.Data.IDbConnection conn, Guid productId, string lotNumber, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT Id FROM inventory.Lots " +
            "WHERE ProductId = @productId AND LotNumber = @lotNumber",
            new { productId, lotNumber },
            cancellationToken: ct));

    private static Task<Guid?> ResolvePalletAsync(
        System.Data.IDbConnection conn, string palletNumber, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT Id FROM inventory.Pallets WHERE PalletNumber = @palletNumber",
            new { palletNumber },
            cancellationToken: ct));
}

// Tiny view-shape for the Posted page — not a Domain entity since
// the page only needs OnHand + the human-readable codes.
public sealed record PutawayPostedRow(
    Guid Id, decimal QuantityOnHand, string LocationCode, string ProductCode);

public sealed record PutawayPostedViewModel(
    PutawayPostedRow Source, PutawayPostedRow Destination);
