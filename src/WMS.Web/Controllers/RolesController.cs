using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Auth;
using WMS.BLL.Services.Security;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Security;
using WMS.Web.Filters;
using WMS.Web.ViewModels.Security;

namespace WMS.Web.Controllers;

// Phase 24 T2 — Roles list + Detail with permission-matrix editor.
// System roles (ADMIN/PICKER/PACKER/MANAGER) display read-only — the
// service refuses writes to them per Migration_035 invariant.
//
// Inline-save UX (D2): each checkbox toggle posts /Roles/SetPermission
// with the new flag set for that one (Role, Function) cell. No batch
// Save button — matches modern admin-grid expectations.
[Authorize]
[RequirePermission("SECURITY.ROLES", PermissionAction.View)]
[Route("Roles")]
public sealed class RolesController : Controller
{
    private readonly IRoleRepositoryFactory _roleRepos;
    private readonly IFunctionRepositoryFactory _functionRepos;
    private readonly ISecurityService _security;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public RolesController(
        IRoleRepositoryFactory roleRepos,
        IFunctionRepositoryFactory functionRepos,
        ISecurityService security,
        ITenantContext tenant,
        ICurrentUser currentUser)
    {
        _roleRepos = roleRepos;
        _functionRepos = functionRepos;
        _security = security;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        var rows = await _roleRepos.For(_tenant.RequireTenantId()).GetAllAsync(ct);
        return View(rows);
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct = default)
    {
        var tenantId = _tenant.RequireTenantId();
        var role = await _roleRepos.For(tenantId).GetByIdAsync(id, ct);
        if (role is null) return NotFound();

        var permissions = await _roleRepos.For(tenantId).GetPermissionsForRoleAsync(id, ct);

        // Group by Module for the matrix layout.
        var grouped = permissions
            .GroupBy(p => p.Module)
            .OrderBy(g => g.Key)
            .ToList();

        var vm = new RoleDetailViewModel
        {
            Role = role,
            Groups = grouped.Select(g => new PermissionGroup(
                Module: g.Key,
                Rows: g.OrderBy(r => r.DisplayOrder).ThenBy(r => r.FunctionCode).ToList())).ToList(),
        };
        return View(vm);
    }

    [HttpPost("SetPermission")]
    [ValidateAntiForgeryToken]
    [RequirePermission("SECURITY.ROLES", PermissionAction.Edit)]
    public async Task<IActionResult> SetPermission(
        [FromBody] SetPermissionPostBody body,
        CancellationToken ct = default)
    {
        try
        {
            await _security.SetPermissionAsync(
                _tenant.RequireTenantId(),
                new SetPermissionRequest(
                    RoleId: body.RoleId,
                    FunctionId: body.FunctionId,
                    CanView: body.CanView,
                    CanAdd: body.CanAdd,
                    CanEdit: body.CanEdit,
                    CanDelete: body.CanDelete,
                    CanApprove: body.CanApprove),
                actorId: _currentUser.UserId ?? Guid.Empty,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString(),
                ct);
            return Json(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }
}

// Inline POST body for the permission-cell toggle. Posted from the
// inline-edit JS — each click sends the FULL row (all 5 flags) so the
// service handles the cell as a single atomic set.
public sealed record SetPermissionPostBody(
    Guid RoleId,
    Guid FunctionId,
    bool CanView,
    bool CanAdd,
    bool CanEdit,
    bool CanDelete,
    bool CanApprove);
