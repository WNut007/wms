using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;
using WMS.Web.Services.Mappers;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 1.3 — read-side of the Carrier admin CRUD.
// Write actions live in CarriersController.Admin.cs (partial).
[Authorize]
[Route("Carriers")]
public partial class CarriersController : Controller
{
    private readonly ICarrierRepositoryFactory _repos;
    private readonly ITenantContext _tenant;
    private readonly IValidator<CarrierCreateViewModel> _createValidator;
    private readonly IValidator<CarrierEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public CarriersController(
        ICarrierRepositoryFactory repos,
        ITenantContext tenant,
        IValidator<CarrierCreateViewModel> createValidator,
        IValidator<CarrierEditViewModel> editValidator,
        ICurrentUser currentUser)
    {
        _repos = repos;
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
        string? type = null,
        string sortBy = "code",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new CarrierFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: CarrierStatusMapper.FromWire(status),
            IsActive: null, // not exposed as chip; use Edit form to toggle
            Type: NormaliseType(type),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(search, ct);

        return Json(new
        {
            items = result.Items.Select(c => new
            {
                id            = c.Id,
                code          = c.Code,
                name          = c.Name,
                type          = c.Type,
                country       = c.Country,
                status        = c.Status,
                statusVariant = CarrierStatusMapper.Variant(c.Status),
                displayOrder  = c.DisplayOrder,
                isActive      = c.IsActive,
                updatedAt     = c.UpdatedAt,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all        = counts.All,
                inactive   = counts.Inactive,
                configured = counts.Configured,
                tested     = counts.Tested,
                production = counts.Production,
            },
        });
    }

    // Closed-set Type filter. Whitespace / 'all' / unknown → null.
    private static string? NormaliseType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase)) return null;
        return CarrierCreateViewModel.AllTypes.Contains(value) ? value : null;
    }
}
