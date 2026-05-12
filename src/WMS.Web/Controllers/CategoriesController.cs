using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;
using WMS.Web.Services.Mappers;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 1.4 — read-side of the Category admin CRUD.
// Write actions live in CategoriesController.Admin.cs (partial).
//
// URL is /Categories (user-facing); domain entity is ProductCategory
// (matches existing IProductCategoryRepository naming convention).
[Authorize]
[Route("Categories")]
public partial class CategoriesController : Controller
{
    private readonly IProductCategoryRepositoryFactory _repos;
    private readonly ITenantContext _tenant;
    private readonly IValidator<CategoryCreateViewModel> _createValidator;
    private readonly IValidator<CategoryEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public CategoriesController(
        IProductCategoryRepositoryFactory repos,
        ITenantContext tenant,
        IValidator<CategoryCreateViewModel> createValidator,
        IValidator<CategoryEditViewModel> editValidator,
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
        bool rootsOnly = false,
        string sortBy = "path",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new ProductCategoryFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            IsActive: CategoryStatusMapper.FromWire(status),
            ParentId: null, // not surfaced as chip filter today
            RootsOnly: rootsOnly,
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(search, ct);

        return Json(new
        {
            items = result.Items.Select(c => new
            {
                id           = c.Id,
                code         = c.Code,
                name         = c.Name,
                parentId     = c.ParentId,
                parentName   = c.ParentName,
                path         = c.Path,
                description  = c.Description,
                displayOrder = c.DisplayOrder,
                status       = CategoryStatusMapper.ToWire(c.IsActive),
                updatedAt    = c.UpdatedAt,
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
                roots    = counts.Roots,
            },
        });
    }
}
