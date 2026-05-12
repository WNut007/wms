using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;
using WMS.Web.Services.Mappers;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 1.2 — read-side of the UoM admin CRUD.
// Write actions live in UomsController.Admin.cs (partial).
[Authorize]
[Route("Uoms")]
public partial class UomsController : Controller
{
    private readonly IUomRepositoryFactory _repos;
    private readonly ITenantContext _tenant;
    private readonly IValidator<UomCreateViewModel> _createValidator;
    private readonly IValidator<UomEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public UomsController(
        IUomRepositoryFactory repos,
        ITenantContext tenant,
        IValidator<UomCreateViewModel> createValidator,
        IValidator<UomEditViewModel> editValidator,
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
        var filter = new UomFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            IsActive: UomStatusMapper.FromWire(status),
            // Type is one of Count/Weight/Volume/Length — pass through
            // verbatim. Unknown/blank treated as null (no filter).
            Type: NormaliseType(type),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(search, ct);

        return Json(new
        {
            items = result.Items.Select(u => new
            {
                id        = u.Id,
                code      = u.Code,
                name      = u.Name,
                type      = u.Type,
                isBase    = u.IsBase,
                status    = UomStatusMapper.ToWire(u.IsActive),
                updatedAt = u.UpdatedAt,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all      = counts.All,
                active   = counts.Active,
                inactive = counts.Inactive,
                count    = counts.Count,
                weight   = counts.Weight,
                volume   = counts.Volume,
                length   = counts.Length,
            },
        });
    }

    // Trusted-but-verify on the Type filter wire field. Only the 4
    // CK_UnitsOfMeasure_Type values pass; anything else falls through
    // to null (= no type filter).
    private static string? NormaliseType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase)) return null;
        return UomCreateViewModel.AllTypes.Contains(value) ? value : null;
    }
}
