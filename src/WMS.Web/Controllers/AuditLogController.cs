using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Security;
using WMS.Web.Filters;

namespace WMS.Web.Controllers;

// Phase 24 T3 — AuditLog viewer. Read-only surface (the underlying
// table is immutable per migration 039); SECURITY.AUDIT_LOG View
// permission gates access. MANAGER has this by default per
// Migration_044; ADMIN via BLL bypass.
[Authorize]
[RequirePermission("SECURITY.AUDIT_LOG", PermissionAction.View)]
[Route("AuditLog")]
public sealed class AuditLogController : Controller
{
    private const int DefaultPageSize = 50;

    private readonly IAuditLogRepositoryFactory _repos;
    private readonly ITenantContext _tenant;

    public AuditLogController(
        IAuditLogRepositoryFactory repos,
        ITenantContext tenant)
    {
        _repos = repos;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        // Pre-populate event-type filter dropdown.
        var repo = _repos.For(_tenant.RequireTenantId());
        ViewBag.EventTypes = await repo.GetDistinctEventTypesAsync(ct);
        return View();
    }

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = DefaultPageSize,
        Guid? userId = null,
        string? eventType = null,
        string? entityType = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? search = null,
        CancellationToken ct = default)
    {
        var filter = new AuditLogFilter(
            Page: page,
            PageSize: pageSize,
            UserId: userId,
            EventType: string.IsNullOrWhiteSpace(eventType) ? null : eventType,
            EntityType: string.IsNullOrWhiteSpace(entityType) ? null : entityType,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            Search: string.IsNullOrWhiteSpace(search) ? null : search.Trim());

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(r => new
            {
                id            = r.Id,
                createdAt     = r.CreatedAt,
                userEmail     = r.UserEmail ?? "(system)",
                userFullName  = r.UserFullName ?? "",
                eventType     = r.EventType,
                entityType    = r.EntityType ?? "",
                entityId      = r.EntityId,
                ipAddress     = r.IpAddress ?? "",
                hasDetails    = !string.IsNullOrEmpty(r.Details),
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
        });
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct = default)
    {
        var repo = _repos.For(_tenant.RequireTenantId());
        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return NotFound();
        return View(row);
    }
}
