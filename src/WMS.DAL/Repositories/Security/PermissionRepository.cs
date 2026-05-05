using System.Data;
using Dapper;
using WMS.Common.Auth;

namespace WMS.DAL.Repositories.Security;

// Resolves every effective (Function, Action) permission for one user
// in a single round-trip. The SQL aggregates flags with MAX so a user
// holding multiple roles gets the union of grants — any role with
// CanView wins over the others' zeros. The C# half then expands one
// row-with-flags into up to five UserPermission tuples.
//
// Inactive functions (security.Functions.IsActive = 0) are excluded
// from the result; revoking a function disables it everywhere without
// having to delete row-level grants.
internal sealed class PermissionRepository : IPermissionRepository
{
    private readonly IDbConnection _connection;

    public PermissionRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<UserPermission>> GetForUserAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var rows = await _connection.QueryAsync<FunctionPermissionRow>(new CommandDefinition(
            @"SELECT f.Code AS FunctionCode,
                     MAX(CASE WHEN rfp.CanView    = 1 THEN 1 ELSE 0 END) AS CanView,
                     MAX(CASE WHEN rfp.CanAdd     = 1 THEN 1 ELSE 0 END) AS CanAdd,
                     MAX(CASE WHEN rfp.CanEdit    = 1 THEN 1 ELSE 0 END) AS CanEdit,
                     MAX(CASE WHEN rfp.CanDelete  = 1 THEN 1 ELSE 0 END) AS CanDelete,
                     MAX(CASE WHEN rfp.CanApprove = 1 THEN 1 ELSE 0 END) AS CanApprove
              FROM security.UserRoles ur
              JOIN security.RoleFunctionPermissions rfp ON rfp.RoleId = ur.RoleId
              JOIN security.Functions f ON f.Id = rfp.FunctionId
              WHERE ur.UserId = @userId
                AND f.IsActive = 1
              GROUP BY f.Code",
            new { userId },
            cancellationToken: ct));

        var perms = new List<UserPermission>();
        foreach (var row in rows)
        {
            if (row.CanView)    perms.Add(new UserPermission(row.FunctionCode, PermissionAction.View));
            if (row.CanAdd)     perms.Add(new UserPermission(row.FunctionCode, PermissionAction.Add));
            if (row.CanEdit)    perms.Add(new UserPermission(row.FunctionCode, PermissionAction.Edit));
            if (row.CanDelete)  perms.Add(new UserPermission(row.FunctionCode, PermissionAction.Delete));
            if (row.CanApprove) perms.Add(new UserPermission(row.FunctionCode, PermissionAction.Approve));
        }
        return perms;
    }

    private sealed class FunctionPermissionRow
    {
        public string FunctionCode { get; set; } = "";
        public bool CanView { get; set; }
        public bool CanAdd { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanApprove { get; set; }
    }
}
