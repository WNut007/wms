using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Web.Services;
using WMS.Web.Services.Mock;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

[Authorize]
[Route("Products")]
public class ProductsController : Controller
{
    private readonly MockProductDataService _data;
    private readonly IDocumentStorageService _docs;

    public ProductsController(MockProductDataService data, IDocumentStorageService docs)
    {
        _data = data;
        _docs = docs;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public IActionResult GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? category = null,
        string? brand = null,
        string sortBy = "name",
        bool sortDesc = false)
    {
        var result = _data.GetProducts(page, pageSize, search, status, category, brand, sortBy, sortDesc);
        return Json(new
        {
            items = result.Items.Select(p => new
            {
                sku               = p.Sku,
                name              = p.Name,
                brand             = p.Brand,
                category          = p.Category,
                iconClass         = p.IconClass,
                iconColor         = p.IconColor,
                price             = p.Price,
                stockOnHand       = p.StockOnHand,
                status            = p.Status,
                updatedAt         = p.UpdatedAt,
                updatedAtRelative = RelativeTime.Format(p.UpdatedAt),
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
        var product = _data.GetBySku(sku);
        if (product == null) return NotFound();

        var docs = await _docs.ListByEntityAsync("Product", sku, ct);

        // Image tiles are now fetched client-side by _ImagesPanel directly
        // from /Documents/List?kind=image — keeps a single source of truth
        // and avoids re-rendering the same data twice on upload/delete.
        var (statusLabel, statusVariant) = product.Status switch
        {
            "active"       => ("Active", "success"),
            "out_of_stock" => ("Out of stock", "warning"),
            _              => ("Discontinued", "neutral"),
        };

        var vm = new DetailPageViewModel
        {
            EntityType = "Product",
            EntityId = product.Sku,
            Title = product.Name,
            Subtitle = $"{product.Sku} · {product.Brand} · {product.Category}",
            IconClass = product.IconClass,
            IconBgColor = $"{product.IconColor}1A",
            IconFgColor = product.IconColor,
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Products",
            BreadcrumbParentUrl = "/Products",
            Stats = new()
            {
                new("Price",     $"฿{product.Price:N0}", "#534AB7"),
                new("Stock",     product.StockOnHand.ToString("N0")),
                new("Reserved",  "23"),
                new("Sold YTD",  "3,142"),
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
            Activities = new()
            {
                new("<span style=\"font-weight:500\">System Admin</span> uploaded 3 product images",
                    "Front, Back, Left angles", "ti-photo-plus", "#534AB7",
                    DateTime.UtcNow.AddHours(-2), "2 h ago", "Today"),
                new("<span style=\"font-weight:500\">System Admin</span> updated price",
                    "Adjustment recorded", "ti-edit", "#BA7517",
                    DateTime.UtcNow.AddHours(-5), "5 h ago", "Today",
                    "฿65,900", "฿69,900"),
                new("<span style=\"font-weight:500\">Maya Rodriguez</span> received 50 units",
                    $"RC-2026-0142 · WH-MAIN · Stock: {product.StockOnHand - 50:N0} → {product.StockOnHand:N0}",
                    "ti-package-import", "#639922",
                    DateTime.UtcNow.AddDays(-1), "1 d ago", "Yesterday"),
                new("<span style=\"font-weight:500\">System Admin</span> uploaded document",
                    "Pricing_Tiers_2026.xlsx · 412 KB", "ti-file-plus", "#534AB7",
                    DateTime.UtcNow.AddDays(-1).AddHours(-3), "1 d ago", "Yesterday"),
                new("<span style=\"font-weight:500\">System Admin</span> created product",
                    $"{product.Sku} added to catalog", "ti-plus", "#888780",
                    DateTime.UtcNow.AddDays(-30), "1 mo ago", "1 month ago"),
            },
            QuickActions = new()
            {
                new("Receive stock",  "ti-package-import", "#"),
                new("Adjust stock",   "ti-adjustments",    "#"),
                new("Print label",    "ti-printer",        "#"),
            },
            OverviewFields = new()
            {
                new("SKU",        $"<span style=\"font-family: var(--wms-font-mono);\">{product.Sku}</span>"),
                new("Brand",      System.Net.WebUtility.HtmlEncode(product.Brand)),
                new("Category",   $"<span class=\"wms-badge wms-badge-info\">{System.Net.WebUtility.HtmlEncode(product.Category)}</span>"),
                new("Barcode",    "<span style=\"font-family: var(--wms-font-mono);\">8806094938371</span>"),
                new("Weight",     "1.55 kg"),
                new("Dimensions", "31.26 × 22.12 × 1.55 cm"),
            },
            Properties = new()
            {
                new("Created", "3 mo ago"),
                new("Updated", RelativeTime.Format(product.UpdatedAt)),
                new("Owner",   "System"),
            }
        };

        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

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
