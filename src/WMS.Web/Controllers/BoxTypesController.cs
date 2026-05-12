using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;
using WMS.Web.Services.Mappers;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 1 — read-side of the BoxTypes admin CRUD.
// Admin write actions live in BoxTypesController.Admin.cs (partial).
[Authorize]
[Route("BoxTypes")]
public partial class BoxTypesController : Controller
{
    private readonly IBoxTypeRepositoryFactory _repos;
    private readonly ITenantContext _tenant;
    private readonly IValidator<BoxTypeCreateViewModel> _createValidator;
    private readonly IValidator<BoxTypeEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public BoxTypesController(
        IBoxTypeRepositoryFactory repos,
        ITenantContext tenant,
        IValidator<BoxTypeCreateViewModel> createValidator,
        IValidator<BoxTypeEditViewModel> editValidator,
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
        string sortBy = "code",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new BoxTypeFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            IsActive: BoxTypeStatusMapper.FromWire(status),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(search, ct);

        return Json(new
        {
            items = result.Items.Select(b => new
            {
                id                    = b.Id,
                code                  = b.Code,
                name                  = b.Name,
                internalLengthCm      = b.InternalLengthCm,
                internalWidthCm       = b.InternalWidthCm,
                internalHeightCm      = b.InternalHeightCm,
                externalLengthCm      = b.ExternalLengthCm,
                externalWidthCm       = b.ExternalWidthCm,
                externalHeightCm      = b.ExternalHeightCm,
                emptyWeightKg         = b.EmptyWeightKg,
                maxLoadKg             = b.MaxLoadKg,
                unitCost              = b.UnitCost,
                status                = BoxTypeStatusMapper.ToWire(b.IsActive),
                updatedAt             = b.UpdatedAt,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new { all = counts.All, active = counts.Active, inactive = counts.Inactive },
        });
    }
}
