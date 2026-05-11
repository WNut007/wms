using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Auth;
using WMS.BLL.Services.Security;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Security;
using WMS.Web.Filters;
using WMS.Web.Services.Mappers;
using WMS.Web.ViewModels.Security;

namespace WMS.Web.Controllers;

// Phase 24 — admin CRUD on security.Users. SECURITY.USERS permission
// gating per action (View on Index/Detail; Add on Create; Edit on the
// rest). Last-admin + self-deactivation invariants live in
// SecurityService — controller only catches the InvalidOperation and
// renders the message into TempData.
[Authorize]
[RequirePermission("SECURITY.USERS", PermissionAction.View)]
[Route("Users")]
public sealed class UsersController : Controller
{
    private const int PageSize = 20;

    private readonly IUserRepositoryFactory _userRepos;
    private readonly IUserRoleRepositoryFactory _userRoleRepos;
    private readonly IRoleRepositoryFactory _roleRepos;
    private readonly ISecurityService _security;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<UserCreateViewModel> _createValidator;
    private readonly IValidator<UserEditViewModel> _editValidator;

    public UsersController(
        IUserRepositoryFactory userRepos,
        IUserRoleRepositoryFactory userRoleRepos,
        IRoleRepositoryFactory roleRepos,
        ISecurityService security,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<UserCreateViewModel> createValidator,
        IValidator<UserEditViewModel> editValidator)
    {
        _userRepos = userRepos;
        _userRoleRepos = userRoleRepos;
        _roleRepos = roleRepos;
        _security = security;
        _tenant = tenant;
        _currentUser = currentUser;
        _createValidator = createValidator;
        _editValidator = editValidator;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = PageSize,
        string? search = null,
        string? status = null,
        string? roleCode = null,
        string sortBy = "email",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new UserFilter(
            Page: page,
            PageSize: pageSize,
            Search: string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Status: UserStatusMapper.FromWire(status),
            RoleCode: string.IsNullOrWhiteSpace(roleCode) ? null : roleCode.Trim(),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _userRepos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(r => new
            {
                id          = r.Id,
                email       = r.Email,
                fullName    = r.FullName ?? "",
                status      = StatusLabel(r),
                statusVariant = UserStatusMapper.Variant(StatusLabel(r)),
                roleCodes   = r.RoleCodes ?? "",
                lastLoginAt = r.LastLoginAt,
                createdAt   = r.CreatedAt,
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
                locked   = counts.Locked,
            },
        });
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct = default)
    {
        var tenantId = _tenant.RequireTenantId();
        var users = _userRepos.For(tenantId);
        var user = await users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        var roles = await _userRoleRepos.For(tenantId).GetByUserAsync(id, ct);
        var vm = new UserDetailViewModel
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            IsActive = user.IsActive,
            LastLoginAt = user.LastLoginAt,
            FailedLoginAttempts = user.FailedLoginAttempts,
            LockedUntil = user.LockedUntil,
            ApprovalLimit = user.ApprovalLimit,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            Roles = roles.Select(r => new AssignedRoleRow(
                r.RoleId, r.RoleCode, r.RoleName, r.IsSystemRole, r.CreatedAt)).ToList(),
            IsCurrentUser = _currentUser.UserId == user.Id,
        };
        return View(vm);
    }

    [HttpGet("Create")]
    [RequirePermission("SECURITY.USERS", PermissionAction.Add)]
    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        await PopulateRoleListAsync(ct);
        return View(new UserCreateViewModel());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    [RequirePermission("SECURITY.USERS", PermissionAction.Add)]
    public async Task<IActionResult> Create(
        UserCreateViewModel model,
        CancellationToken ct = default)
    {
        var fv = await _createValidator.ValidateAsync(model, ct);
        if (!fv.IsValid)
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);

        if (!ModelState.IsValid)
        {
            await PopulateRoleListAsync(ct);
            return View(model);
        }

        try
        {
            var newId = await _security.CreateUserAsync(
                _tenant.RequireTenantId(),
                new CreateUserRequest(
                    Email: model.Email,
                    Password: model.Password,
                    FullName: model.FullName,
                    ApprovalLimit: model.ApprovalLimit,
                    RoleIds: model.RoleIds),
                actorId: _currentUser.UserId ?? Guid.Empty,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString(),
                ct);

            TempData["UserMessage"] = $"User '{model.Email}' created.";
            return RedirectToAction(nameof(Detail), new { id = newId });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateRoleListAsync(ct);
            return View(model);
        }
    }

    [HttpGet("Edit/{id:guid}")]
    [RequirePermission("SECURITY.USERS", PermissionAction.Edit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct = default)
    {
        var tenantId = _tenant.RequireTenantId();
        var user = await _userRepos.For(tenantId).GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        var existingRoles = await _userRoleRepos.For(tenantId).GetRoleIdsByUserAsync(id, ct);
        await PopulateRoleListAsync(ct);

        var vm = new UserEditViewModel
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            ApprovalLimit = user.ApprovalLimit,
            RoleIds = existingRoles.ToList(),
        };
        return View(vm);
    }

    [HttpPost("Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequirePermission("SECURITY.USERS", PermissionAction.Edit)]
    public async Task<IActionResult> Edit(
        Guid id,
        UserEditViewModel model,
        CancellationToken ct = default)
    {
        model.Id = id;  // route wins over body to guard against tampering

        var fv = await _editValidator.ValidateAsync(model, ct);
        if (!fv.IsValid)
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);

        if (!ModelState.IsValid)
        {
            await PopulateRoleListAsync(ct);
            return View(model);
        }

        try
        {
            await _security.UpdateUserAsync(
                _tenant.RequireTenantId(),
                new UpdateUserRequest(
                    Id: model.Id,
                    Email: model.Email,
                    FullName: model.FullName,
                    ApprovalLimit: model.ApprovalLimit,
                    RoleIds: model.RoleIds),
                actorId: _currentUser.UserId ?? Guid.Empty,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString(),
                ct);

            TempData["UserMessage"] = $"User '{model.Email}' updated.";
            return RedirectToAction(nameof(Detail), new { id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateRoleListAsync(ct);
            return View(model);
        }
    }

    [HttpPost("ToggleActive/{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequirePermission("SECURITY.USERS", PermissionAction.Edit)]
    public async Task<IActionResult> ToggleActive(
        Guid id,
        bool isActive,
        CancellationToken ct = default)
    {
        try
        {
            await _security.ToggleUserActiveAsync(
                _tenant.RequireTenantId(),
                id,
                isActive,
                actorId: _currentUser.UserId ?? Guid.Empty,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString(),
                ct);
            TempData["UserMessage"] = isActive ? "User activated." : "User deactivated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["UserError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Unlock/{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequirePermission("SECURITY.USERS", PermissionAction.Edit)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct = default)
    {
        try
        {
            await _security.UnlockUserAsync(
                _tenant.RequireTenantId(),
                id,
                actorId: _currentUser.UserId ?? Guid.Empty,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString(),
                ct);
            TempData["UserMessage"] = "User unlocked.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["UserError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detail), new { id });
    }

    private static string StatusLabel(UserListRow row)
    {
        if (row.LockedUntil is not null && row.LockedUntil > DateTime.UtcNow) return "Locked";
        return row.IsActive ? "Active" : "Inactive";
    }

    private async Task PopulateRoleListAsync(CancellationToken ct)
    {
        var roles = await _roleRepos.For(_tenant.RequireTenantId()).GetActiveAsync(ct);
        ViewBag.Roles = roles;
    }
}
