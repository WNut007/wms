using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

// Read-side (Index, GetData, Detail) lives in this file. Phase 7
// admin Create / Edit lives in ProductsController.Admin.cs (partial)
// to keep individual files under the CLAUDE.md 200-line guideline.
[Authorize]
[Route("Products")]
public partial class ProductsController : Controller
{
    // Cap matches the _ActivityPanel header copy ("Last 30 days") and
    // gives the timeline enough breathing room without paging.
    private const int ActivityFeedLimit = 20;

    private readonly IProductRepositoryFactory _repos;
    private readonly IStockMovementRepositoryFactory _movementRepos;
    private readonly ITenantContext _tenant;
    private readonly IDocumentStorageService _docs;
    private readonly IProductCategoryRepositoryFactory _categoryRepos;
    private readonly IUomRepositoryFactory _uomRepos;
    private readonly IValidator<ProductCreateViewModel> _createValidator;
    private readonly IValidator<ProductEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public ProductsController(
        IProductRepositoryFactory repos,
        IStockMovementRepositoryFactory movementRepos,
        ITenantContext tenant,
        IDocumentStorageService docs,
        IProductCategoryRepositoryFactory categoryRepos,
        IUomRepositoryFactory uomRepos,
        IValidator<ProductCreateViewModel> createValidator,
        IValidator<ProductEditViewModel> editValidator,
        ICurrentUser currentUser)
    {
        _repos = repos;
        _movementRepos = movementRepos;
        _tenant = tenant;
        _docs = docs;
        _categoryRepos = categoryRepos;
        _uomRepos = uomRepos;
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
        string? category = null,
        string? brand = null,
        string sortBy = "name",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new ProductFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            // Wire format → DB-canonical PascalCase. Unknown wire values
            // (incl. mock 'out_of_stock') are dropped to no-filter.
            Status: ProductStatusMapper.FromWire(status),
            // 'all' / null → no filter; otherwise pass through to match
            // master.ProductCategories.Code. Mock UI sends category names;
            // until categories are seeded with matching Codes, most
            // category filters return empty (acceptable for Phase 6B).
            CategoryCode: NormaliseFilter(category),
            Brand: NormaliseFilter(brand),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var result = await _repos.For(_tenant.RequireTenantId())
                                 .GetPagedAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(p =>
            {
                var (icon, color) = CategoryIconResolver.Resolve(p.CategoryCode);
                return new
                {
                    sku               = p.Code,
                    name              = p.Name,
                    brand             = p.Brand,
                    category          = p.CategoryCode,
                    iconClass         = icon,
                    iconColor         = color,
                    // Pricing lives on ProductOwners.SettlementPrice
                    // (owner-scoped, not a single per-product number).
                    // Surfaced as null here; the JS list view renders
                    // '—' rather than '฿NaN'. TD-012.
                    price             = (decimal?)null,
                    stockOnHand       = p.StockOnHand,
                    status            = ProductStatusMapper.ToWire(p.Status),
                    updatedAt         = p.UpdatedAt,
                    updatedAtRelative = p.UpdatedAt is null
                        ? "—"
                        : RelativeTime.Format(p.UpdatedAt.Value),
                };
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
        });
    }

    [HttpGet("Detail/{sku}")]
    public async Task<IActionResult> Detail(string sku, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var row = await _repos.For(tenantId).GetListRowByCodeAsync(sku, ct);
        if (row is null) return NotFound();

        var docs = await _docs.ListByEntityAsync("Product", sku, ct);

        // Closes TD-010. GetByProductAsync JOINs through inventory.Stock
        // by ProductId and returns up to ActivityFeedLimit movements
        // newest-first. Empty list is the expected state for fresh
        // products until receiving / putaway writes movement rows;
        // _ActivityPanel renders its own empty state in that case.
        var movements = await _movementRepos.For(tenantId)
                                            .GetByProductAsync(row.Id, since: null, limit: ActivityFeedLimit, ct);

        var (statusLabel, statusVariant) = row.Status switch
        {
            "Active"       => ("Active", "success"),
            "Inactive"     => ("Inactive", "neutral"),
            "Draft"        => ("Draft", "warning"),
            _              => ("Discontinued", "neutral"),
        };

        var (iconClass, iconColor) = CategoryIconResolver.Resolve(row.CategoryCode);

        var vm = new DetailPageViewModel
        {
            EntityType = "Product",
            EntityId = row.Code,
            Title = row.Name,
            Subtitle = $"{row.Code} · {row.Brand ?? "—"} · {row.CategoryCode}",
            IconClass = iconClass,
            IconBgColor = $"{iconColor}1A",
            IconFgColor = iconColor,
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Products",
            BreadcrumbParentUrl = "/Products",
            Stats = new()
            {
                // Price intentionally omitted — see TD-012. Owner-scoped
                // pricing lives on ProductOwners.SettlementPrice.
                new("Stock",     row.StockOnHand.ToString("N0")),
                // Reserved + Sold YTD remain stubs — orders schema and
                // reservation tracking land in Phase 7+ (TD-011-adjacent).
                new("Reserved",  "—"),
                new("Sold YTD",  "—"),
            },
            ShowImagesTab = true,
            Documents = docs.Select(d => new DocumentItem(
                d.DocumentId,
                d.FileName,
                d.Category,
                CategoryColorBg(d.Category),
                CategoryColorFg(d.Category),
                d.IconClass,
                d.IconColorBg,
                d.IconColorFg,
                d.FileSizeFormatted,
                d.UploadedBy,
                d.UploadedAt,
                RelativeTime.Format(d.UploadedAt)
            )).ToList(),
            // Real movement history (Phase 6C — TD-010 closed). Empty
            // until receiving/putaway writes movements; _ActivityPanel
            // renders "No activity yet." for an empty list.
            Activities = movements.Select(MovementActivityMapper.Map).ToList(),
            QuickActions = new()
            {
                // TD-017 partial — wired now that ReceiveController.Index
                // accepts ?sku=. The other two stay disabled until
                // Phase 7+ admin CRUD provides their target routes.
                new("Receive stock",  "ti-package-import",
                    $"/receive?sku={Uri.EscapeDataString(row.Code)}"),
                new("Adjust stock",   "ti-adjustments",    "#", Enabled: false),
                new("Print label",    "ti-printer",        "#", Enabled: false),
            },
            OverviewFields = new()
            {
                new("SKU",        $"<span style=\"font-family: var(--wms-font-mono);\">{System.Net.WebUtility.HtmlEncode(row.Code)}</span>"),
                new("Brand",      System.Net.WebUtility.HtmlEncode(row.Brand ?? "—")),
                new("Category",   $"<span class=\"wms-badge wms-badge-info\">{System.Net.WebUtility.HtmlEncode(row.CategoryCode)}</span>"),
                // Barcode/Weight/Dimensions live on master.ProductBarcodes
                // / future schema columns. Stubbed until those land.
                new("Barcode",    "—"),
                new("Weight",     "—"),
                new("Dimensions", "—"),
            },
            Properties = new()
            {
                new("Created", RelativeTime.Format(row.CreatedAt)),
                new("Updated", row.UpdatedAt is null ? "—" : RelativeTime.Format(row.UpdatedAt.Value)),
                new("Owner",   "System"),
            }
        };

        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    // 'all' / null / whitespace → null (= no filter applied).
    private static string? NormaliseFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static string CategoryColorBg(string cat) => cat switch
    {
        "Specification" => "#E6F1FB",
        "Manual"        => "#EEEDFE",
        "Pricing"       => "#FAEEDA",
        "Certificate"   => "#E1F5EE",
        "Contract"      => "#FCEBEB",
        _               => "#F1EFE8",
    };

    private static string CategoryColorFg(string cat) => cat switch
    {
        "Specification" => "#0C447C",
        "Manual"        => "#3C3489",
        "Pricing"       => "#854F0B",
        "Certificate"   => "#085041",
        "Contract"      => "#A32D2D",
        _               => "#444441",
    };
}
