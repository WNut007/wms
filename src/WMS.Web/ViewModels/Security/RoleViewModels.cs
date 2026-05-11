using WMS.DAL.Repositories.Security;
using WMS.Domain.Entities.Security;

namespace WMS.Web.ViewModels.Security;

public sealed class RoleDetailViewModel
{
    public Role Role { get; set; } = new();
    public IReadOnlyList<PermissionGroup> Groups { get; set; } = Array.Empty<PermissionGroup>();
}

public sealed record PermissionGroup(
    string Module,
    IReadOnlyList<RolePermissionRow> Rows);
