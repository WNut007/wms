using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Filters;
using WMS.Web.Services.SuperAdmin;
using WMS.Web.ViewModels.SuperAdmin;

namespace WMS.Web.Controllers;

// Phase 27 — SuperAdmin tenant CRUD console. Gated on RequireSuperAdmin
// so all actions require the SuperAdminAuth cookie scheme.
//
// MVP scope per brief D7: Dashboard + Tenants list/create/detail +
// Suspend/Reactivate + Reset admin password. Per-tenant stats (TD-082),
// settings UI (TD-083), cloning (TD-084), export (TD-085) deferred.
[RequireSuperAdmin]
[Route("SuperAdmin")]
public sealed class SuperAdminController : Controller
{
    private readonly IMasterConnectionFactory _masterFactory;
    private readonly ISystemAuditLogRepository _auditRepo;
    private readonly ITenantProvisioningService _provisioning;

    public SuperAdminController(
        IMasterConnectionFactory masterFactory,
        ISystemAuditLogRepository auditRepo,
        ITenantProvisioningService provisioning)
    {
        _masterFactory = masterFactory;
        _auditRepo = auditRepo;
        _provisioning = provisioning;
    }

    [HttpGet("Dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct = default)
    {
        using var conn = _masterFactory.CreateConnection();

        // Quick aggregates for the dashboard tiles.
        var counts = await conn.QuerySingleAsync<(int Total, int Active, int Suspended, int Inactive)>(
            new CommandDefinition(@"
-- Phase 29 — ISNULL on SUM so an empty master.Tenants returns
-- 0 across all 4 tiles. Without it, SUM on 0 rows returns NULL
-- and Dapper throws InvalidCastException on the int tuple.
SELECT
    COUNT(*) AS Total,
    ISNULL(SUM(CASE WHEN Status = 'Active'    THEN 1 ELSE 0 END), 0) AS Active,
    ISNULL(SUM(CASE WHEN Status = 'Suspended' THEN 1 ELSE 0 END), 0) AS Suspended,
    ISNULL(SUM(CASE WHEN Status = 'Inactive'  THEN 1 ELSE 0 END), 0) AS Inactive
FROM [master].[Tenants];", cancellationToken: ct));

        // Recent SuperAdmin-emitted audit events (last 10) for the
        // activity feed.
        var recentAudit = await _auditRepo.GetPagedAsync(
            new SystemAuditLogFilter(Page: 1, PageSize: 10), ct);

        ViewBag.Counts = counts;
        ViewBag.RecentAudit = recentAudit.Items;
        return View();
    }

    [HttpGet("Tenants")]
    public async Task<IActionResult> Tenants(string? status = null, CancellationToken ct = default)
    {
        using var conn = _masterFactory.CreateConnection();

        var rows = (await conn.QueryAsync<TenantListRow>(new CommandDefinition(@"
SELECT
    t.Id, t.Code, t.Name, t.DatabaseName, t.Status, t.CreatedAt,
    (SELECT COUNT(*) FROM [master].[UserTenantMap] m WHERE m.TenantId = t.Id) AS UserCount
FROM [master].[Tenants] t
WHERE (@status IS NULL OR t.Status = @status)
ORDER BY t.CreatedAt DESC;",
            new { status }, cancellationToken: ct))).ToList();

        var counts = await conn.QuerySingleAsync<(int Active, int Suspended, int Inactive)>(
            new CommandDefinition(@"
-- Phase 29 — see Dashboard for ISNULL rationale.
SELECT
    ISNULL(SUM(CASE WHEN Status = 'Active'    THEN 1 ELSE 0 END), 0) AS Active,
    ISNULL(SUM(CASE WHEN Status = 'Suspended' THEN 1 ELSE 0 END), 0) AS Suspended,
    ISNULL(SUM(CASE WHEN Status = 'Inactive'  THEN 1 ELSE 0 END), 0) AS Inactive
FROM [master].[Tenants];", cancellationToken: ct));

        return View(new TenantListViewModel
        {
            Rows = rows,
            CountActive = counts.Active,
            CountSuspended = counts.Suspended,
            CountInactive = counts.Inactive,
            StatusFilter = status,
        });
    }

    [HttpGet("Tenants/Create")]
    public IActionResult CreateTenant() => View(new TenantCreateViewModel());

    [HttpPost("Tenants/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTenant(
        TenantCreateViewModel model,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(model);

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return RedirectToAction("Login", "SuperAdminAuth");

        try
        {
            var result = await _provisioning.CreateTenantAsync(
                code: model.Code,
                name: model.Name,
                adminEmail: model.AdminEmail,
                adminFullName: model.AdminFullName,
                actorSuperAdminId: actorId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString(),
                ct);

            // TempData carries the temp password through the redirect.
            // Displayed ONCE on the success page; never logged.
            TempData["TempPassword"] = result.AdminTempPassword;
            TempData["TempPasswordCode"] = result.Code;
            TempData["TempPasswordAdminEmail"] = result.AdminEmail;
            TempData["TempPasswordDbName"] = result.DatabaseName;
            TempData["TempPasswordTenantId"] = result.TenantId.ToString();

            return RedirectToAction(nameof(CreateTenantSuccess));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet("Tenants/Created")]
    public IActionResult CreateTenantSuccess()
    {
        var tempPassword = TempData["TempPassword"] as string;
        if (string.IsNullOrEmpty(tempPassword))
            return RedirectToAction(nameof(Tenants));

        var vm = new TenantCreateSuccessViewModel
        {
            TenantId = Guid.TryParse(TempData["TempPasswordTenantId"] as string, out var tid) ? tid : Guid.Empty,
            Code = TempData["TempPasswordCode"] as string ?? "",
            Name = "", // not carried, redirect to Detail to see
            DatabaseName = TempData["TempPasswordDbName"] as string ?? "",
            AdminEmail = TempData["TempPasswordAdminEmail"] as string ?? "",
            AdminTempPassword = tempPassword,
        };
        return View(vm);
    }

    [HttpGet("Tenants/{id:guid}")]
    public async Task<IActionResult> TenantDetail(Guid id, CancellationToken ct = default)
    {
        using var conn = _masterFactory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<TenantDetailViewModel>(
            new CommandDefinition(@"
SELECT
    t.Id, t.Code, t.Name, t.DatabaseName, t.Status, t.CreatedAt, t.UpdatedAt,
    (SELECT COUNT(*) FROM [master].[UserTenantMap] m WHERE m.TenantId = t.Id) AS UserCount,
    (SELECT TOP 1 m.UserEmail FROM [master].[UserTenantMap] m
       WHERE m.TenantId = t.Id AND m.IsDefault = 1) AS AdminEmail
FROM [master].[Tenants] t
WHERE t.Id = @id;",
                new { id }, cancellationToken: ct));

        if (row is null) return NotFound();
        return View(row);
    }

    [HttpPost("Tenants/{id:guid}/Suspend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(Guid id, SuspendTenantViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["SuperAdminError"] = "Suspension reason is required (3+ chars).";
            return RedirectToAction(nameof(TenantDetail), new { id });
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return RedirectToAction("Login", "SuperAdminAuth");

        try
        {
            await _provisioning.SuspendAsync(id, model.Reason, actorId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(), ct);
            TempData["SuperAdminMessage"] = "Tenant suspended.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SuperAdminError"] = ex.Message;
        }
        return RedirectToAction(nameof(TenantDetail), new { id });
    }

    [HttpPost("Tenants/{id:guid}/Reactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return RedirectToAction("Login", "SuperAdminAuth");

        try
        {
            await _provisioning.ReactivateAsync(id, actorId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(), ct);
            TempData["SuperAdminMessage"] = "Tenant reactivated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SuperAdminError"] = ex.Message;
        }
        return RedirectToAction(nameof(TenantDetail), new { id });
    }

    [HttpPost("Tenants/{id:guid}/ResetAdminPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAdminPassword(Guid id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return RedirectToAction("Login", "SuperAdminAuth");

        try
        {
            var newPassword = await _provisioning.ResetTenantAdminPasswordAsync(
                id, actorId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(), ct);
            TempData["ResetAdminTempPassword"] = newPassword;
        }
        catch (InvalidOperationException ex)
        {
            TempData["SuperAdminError"] = ex.Message;
        }
        return RedirectToAction(nameof(TenantDetail), new { id });
    }
}
