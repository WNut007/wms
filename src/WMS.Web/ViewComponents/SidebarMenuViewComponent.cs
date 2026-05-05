using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;

namespace WMS.Web.ViewComponents;

// Renders the office-layout sidebar — the modules the current user has
// at least one permission in. Modules are derived from the function
// codes in their permission list (the part before the first dot in
// "INVENTORY.STOCK"), so the menu stays in sync with whichever
// functions security.Functions actually contains — no second source of
// truth to drift.
//
// Display order follows operational flow: inbound → inventory →
// outbound → returns, then counts / billing, then admin (master /
// security / config / reports). Unknown modules sort last alphabetically
// so an out-of-spec function code still surfaces (visibly out of place,
// easy to notice).
//
// Anonymous requests get an empty list — the layout hides the sidebar
// entirely in that branch.
public sealed class SidebarMenuViewComponent : ViewComponent
{
    private static readonly string[] ModuleOrder =
    {
        "INBOUND", "INVENTORY", "OUTBOUND", "RETURNS",
        "COUNTS", "BILLING", "MASTER", "SECURITY",
        "CONFIG", "REPORTS",
    };

    private readonly ICurrentUser _currentUser;
    private readonly IPermissionService _permService;

    public SidebarMenuViewComponent(
        ICurrentUser currentUser,
        IPermissionService permService)
    {
        _currentUser = currentUser;
        _permService = permService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!_currentUser.IsAuthenticated
            || _currentUser.UserId is null
            || _currentUser.TenantId is null)
        {
            return View(Array.Empty<ModuleItem>());
        }

        var perms = await _permService.GetForUserAsync(
            _currentUser.UserId.Value,
            _currentUser.TenantId.Value);

        var modules = perms
            .Select(p => ModuleOf(p.FunctionCode))
            .Where(m => m is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(OrderIndex)
            .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
            .Select(m => new ModuleItem(m, ToTitleCase(m), "#"))
            .ToArray();

        return View(modules);
    }

    private static string? ModuleOf(string functionCode)
    {
        var dot = functionCode.IndexOf('.');
        return dot > 0 ? functionCode[..dot] : null;
    }

    private static int OrderIndex(string moduleCode)
    {
        var idx = Array.IndexOf(ModuleOrder, moduleCode.ToUpperInvariant());
        return idx == -1 ? int.MaxValue : idx;
    }

    private static string ToTitleCase(string code) =>
        code.Length == 0
            ? ""
            : char.ToUpperInvariant(code[0]) + code[1..].ToLowerInvariant();
}

public sealed record ModuleItem(string Code, string DisplayName, string Url);
