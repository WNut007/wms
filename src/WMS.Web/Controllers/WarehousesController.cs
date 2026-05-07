using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Web.Services.Mock;

namespace WMS.Web.Controllers;

[Authorize]
[Route("Warehouses")]
public class WarehousesController : Controller
{
    private readonly MockWarehouseDataService _data;

    public WarehousesController(MockWarehouseDataService data) => _data = data;

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public IActionResult GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? region = null,
        string? type = null,
        string sortBy = "name",
        bool sortDesc = false)
    {
        var result = _data.GetWarehouses(page, pageSize, search, status, region, type, sortBy, sortDesc);
        return Json(new
        {
            items = result.Items.Select(w => new
            {
                code              = w.Code,
                name              = w.Name,
                subtitle          = w.Subtitle,
                region            = w.Region,
                type              = w.Type,
                status            = w.Status,
                locationCount     = w.LocationCount,
                updatedAt         = w.UpdatedAt,
                updatedAtRelative = ToRelativeTime(w.UpdatedAt),
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
        });
    }

    private static string ToRelativeTime(DateTime dt)
    {
        var span = DateTime.UtcNow - dt;
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)   return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7)     return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30)    return $"{(int)(span.TotalDays / 7)}w ago";
        return $"{(int)(span.TotalDays / 30)}mo ago";
    }
}
