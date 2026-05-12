using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;
using WMS.Web.Services.Mappers;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 2.2 — read-side of the Location admin CRUD.
// Write actions live in LocationsController.Admin.cs (partial).
//
// Composite-key uniqueness (WarehouseId, Code). Edit route keyed by
// Guid (same pattern as Zones). Cascade picker: Warehouse → Zone.
[Authorize]
[Route("Locations")]
public partial class LocationsController : Controller
{
    private readonly ILocationRepositoryFactory _repos;
    private readonly IWarehouseRepositoryFactory _warehouseRepos;
    private readonly ITenantContext _tenant;
    private readonly IValidator<LocationCreateViewModel> _createValidator;
    private readonly IValidator<LocationEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public LocationsController(
        ILocationRepositoryFactory repos,
        IWarehouseRepositoryFactory warehouseRepos,
        ITenantContext tenant,
        IValidator<LocationCreateViewModel> createValidator,
        IValidator<LocationEditViewModel> editValidator,
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
        bool? isActive = null,
        Guid? warehouseId = null,
        Guid? zoneId = null,
        string sortBy = "warehouse",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new LocationFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            IsActive: isActive,
            WarehouseId: warehouseId == Guid.Empty ? null : warehouseId,
            ZoneId: zoneId == Guid.Empty ? null : zoneId,
            Status: LocationStatusMapper.FromWire(status),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(search, ct);

        return Json(new
        {
            items = result.Items.Select(l => new
            {
                id             = l.Id,
                warehouseId    = l.WarehouseId,
                warehouseCode  = l.WarehouseCode,
                zoneId         = l.ZoneId,
                zoneCode       = l.ZoneCode,
                zoneType       = l.ZoneType,
                code           = l.Code,
                description    = l.Description,
                capacityPolicy = l.CapacityPolicy,
                binRank        = l.BinRank,
                isPickface     = l.IsPickface,
                status         = l.Status,
                statusVariant  = LocationStatusMapper.Variant(l.Status),
                isActive       = l.IsActive,
                stockRows      = l.StockRows,
                updatedAt      = l.UpdatedAt,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all                = counts.All,
                active             = counts.Active,
                inactive           = counts.Inactive,
                statusActive       = counts.StatusActive,
                statusBlocked      = counts.StatusBlocked,
                statusMaintenance  = counts.StatusMaintenance,
                pickfaces          = counts.Pickfaces,
            },
        });
    }

    // Warehouse picker dropdown source.
    [HttpGet("Warehouses")]
    public async Task<IActionResult> Warehouses(CancellationToken ct)
    {
        var rows = await _warehouseRepos.For(_tenant.RequireTenantId()).GetActiveAsync(ct);
        return Json(rows.Select(w => new { id = w.Id, code = w.Code, name = w.Name }));
    }

    // Cascade picker — zones in a specific warehouse for the Index
    // ZoneId filter AND the Create/Edit form's Zone select onChange.
    [HttpGet("ZonesForWarehouse/{warehouseId:guid}")]
    public async Task<IActionResult> ZonesForWarehouse(Guid warehouseId, CancellationToken ct)
    {
        var rows = await _repos.For(_tenant.RequireTenantId())
                               .GetZonesForWarehouseAsync(warehouseId, ct);
        return Json(rows.Select(z => new { id = z.Id, code = z.Code, name = z.Name }));
    }
}
