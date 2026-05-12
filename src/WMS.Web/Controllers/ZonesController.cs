using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;
using WMS.Web.Services.Mappers;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 2.1 — read-side of the Zone admin CRUD.
// Write actions live in ZonesController.Admin.cs (partial).
//
// Zones are warehouse-scoped (Code uniqueness is per-warehouse). The
// Create form takes a Warehouse dropdown; Edit locks both Warehouse
// and Code.
[Authorize]
[Route("Zones")]
public partial class ZonesController : Controller
{
    private readonly IZoneRepositoryFactory _repos;
    private readonly IWarehouseRepositoryFactory _warehouseRepos;
    private readonly ITenantContext _tenant;
    private readonly IValidator<ZoneCreateViewModel> _createValidator;
    private readonly IValidator<ZoneEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public ZonesController(
        IZoneRepositoryFactory repos,
        IWarehouseRepositoryFactory warehouseRepos,
        ITenantContext tenant,
        IValidator<ZoneCreateViewModel> createValidator,
        IValidator<ZoneEditViewModel> editValidator,
        ICurrentUser currentUser)
    {
        _repos = repos;
        _warehouseRepos = warehouseRepos;
        _tenant = tenant;
        _createValidator = createValidator;
        _editValidator = editValidator;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        Guid? warehouseId = null,
        string? type = null,
        string sortBy = "warehouse",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new ZoneFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            IsActive: ZoneStatusMapper.FromWire(status),
            WarehouseId: warehouseId == Guid.Empty ? null : warehouseId,
            Type: NormaliseType(type),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(search, ct);

        return Json(new
        {
            items = result.Items.Select(z => new
            {
                id                  = z.Id,
                warehouseId         = z.WarehouseId,
                warehouseCode       = z.WarehouseCode,
                warehouseName       = z.WarehouseName,
                code                = z.Code,
                name                = z.Name,
                type                = z.Type,
                temperature         = z.Temperature,
                lotCommingleAllowed = z.LotCommingleAllowed,
                locationCount       = z.LocationCount,
                status              = ZoneStatusMapper.ToWire(z.IsActive),
                updatedAt           = z.UpdatedAt,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all        = counts.All,
                active     = counts.Active,
                inactive   = counts.Inactive,
                receiving  = counts.Receiving,
                storage    = counts.Storage,
                picking    = counts.Picking,
                packing    = counts.Packing,
                shipping   = counts.Shipping,
                staging    = counts.Staging,
                quarantine = counts.Quarantine,
                returns    = counts.Returns,
            },
        });
    }

    // Warehouse picker dropdown source for the Index filter row.
    // (Create/Edit forms use the same data via ViewBag-populated select.)
    [HttpGet("Warehouses")]
    public async Task<IActionResult> Warehouses(CancellationToken ct)
    {
        var rows = await _warehouseRepos.For(_tenant.RequireTenantId()).GetActiveAsync(ct);
        return Json(rows.Select(w => new { id = w.Id, code = w.Code, name = w.Name }));
    }

    private static string? NormaliseType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase)) return null;
        return ZoneCreateViewModel.AllTypes.Contains(value) ? value : null;
    }
}
