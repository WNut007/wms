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
