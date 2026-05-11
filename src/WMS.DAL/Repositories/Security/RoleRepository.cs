using System.Data;
using Dapper;
using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

internal sealed class RoleRepository : IRoleRepository
{
    private const string SelectColumns = @"
        SELECT Id, Code, Name, Description, IsSystemRole, IsActive,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
        FROM security.Roles";

    private readonly IDbConnection _connection;

    public RoleRepository(IDbConnection connection) => _connection = connection;

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Role?>(new CommandDefinition(
            SelectColumns + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Role?>(new CommandDefinition(
            SelectColumns + " WHERE Code = @code",
            new { code },
            cancellationToken: ct));

    public async Task<IReadOnlyList<Role>> GetActiveAsync(CancellationToken ct = default)
    {
        var rows = await _connection.QueryAsync<Role>(new CommandDefinition(
            SelectColumns + " WHERE IsActive = 1 ORDER BY Code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<RoleListRow>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT r.Id, r.Code, r.Name, r.Description, r.IsSystemRole, r.IsActive,
       (SELECT COUNT(*) FROM security.UserRoles ur WHERE ur.RoleId = r.Id) AS UserCount,
       (SELECT COUNT(*) FROM security.RoleFunctionPermissions rfp
        WHERE rfp.RoleId = r.Id
          AND (rfp.CanView = 1 OR rfp.CanAdd = 1 OR rfp.CanEdit = 1
               OR rfp.CanDelete = 1 OR rfp.CanApprove = 1)) AS PermissionCount,
       r.CreatedAt
FROM security.Roles r
ORDER BY r.IsSystemRole DESC, r.Code;";
        var rows = await _connection.QueryAsync<RoleListRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<RolePermissionRow>> GetPermissionsForRoleAsync(
        Guid roleId, CancellationToken ct = default)
    {
        // LEFT JOIN means a Function without a grant row for this role
        // surfaces with all flags = 0 (COALESCEd) — the matrix renders
        // the full Function catalogue without a separate union path.
        const string sql = @"
SELECT f.Id AS FunctionId, f.Code AS FunctionCode, f.Name AS FunctionName,
       f.Module, f.DisplayOrder,
       COALESCE(rfp.CanView, 0) AS CanView,
       COALESCE(rfp.CanAdd, 0) AS CanAdd,
       COALESCE(rfp.CanEdit, 0) AS CanEdit,
       COALESCE(rfp.CanDelete, 0) AS CanDelete,
       COALESCE(rfp.CanApprove, 0) AS CanApprove
FROM security.Functions f
LEFT JOIN security.RoleFunctionPermissions rfp
    ON rfp.FunctionId = f.Id AND rfp.RoleId = @roleId
WHERE f.IsActive = 1
ORDER BY f.Module, f.DisplayOrder, f.Code;";
        var rows = await _connection.QueryAsync<RolePermissionRow>(
            new CommandDefinition(sql, new { roleId }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task UpsertPermissionAsync(
        Guid roleId,
        Guid functionId,
        bool canView,
        bool canAdd,
        bool canEdit,
        bool canDelete,
        bool canApprove,
        Guid? actorId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"MERGE security.RoleFunctionPermissions WITH (HOLDLOCK) AS target
              USING (SELECT @roleId AS RoleId, @functionId AS FunctionId) AS source
              ON target.RoleId = source.RoleId AND target.FunctionId = source.FunctionId
              WHEN MATCHED THEN
                  UPDATE SET CanView = @canView,
                             CanAdd = @canAdd,
                             CanEdit = @canEdit,
                             CanDelete = @canDelete,
                             CanApprove = @canApprove,
                             UpdatedAt = SYSUTCDATETIME(),
                             UpdatedBy = @actorId
              WHEN NOT MATCHED THEN
                  INSERT (Id, RoleId, FunctionId, CanView, CanAdd, CanEdit, CanDelete, CanApprove,
                          CreatedAt, CreatedBy)
                  VALUES (NEWID(), @roleId, @functionId, @canView, @canAdd, @canEdit, @canDelete, @canApprove,
                          SYSUTCDATETIME(), @actorId);",
            new { roleId, functionId, canView, canAdd, canEdit, canDelete, canApprove, actorId },
            cancellationToken: ct));
}
