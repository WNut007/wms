using System.Data;
using Dapper;
using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

internal sealed class UserRoleRepository : IUserRoleRepository
{
    private readonly IDbConnection _connection;

    public UserRoleRepository(IDbConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<UserRoleAssignment>> GetByUserAsync(
        Guid userId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT ur.Id, ur.UserId, ur.RoleId,
       r.Code AS RoleCode, r.Name AS RoleName, r.IsSystemRole,
       ur.ValidFrom, ur.ValidTo, ur.AssignedBy, ur.CreatedAt
FROM security.UserRoles ur
INNER JOIN security.Roles r ON r.Id = ur.RoleId
WHERE ur.UserId = @userId
ORDER BY r.Code;";
        var rows = await _connection.QueryAsync<UserRoleAssignment>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Guid>> GetRoleIdsByUserAsync(
        Guid userId, CancellationToken ct = default)
    {
        var rows = await _connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT RoleId FROM security.UserRoles WHERE UserId = @userId",
            new { userId },
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task AddAsync(UserRole assignment, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO security.UserRoles
                (Id, UserId, RoleId, ValidFrom, ValidTo, AssignedBy, CreatedAt, CreatedBy)
              VALUES
                (@Id, @UserId, @RoleId, @ValidFrom, @ValidTo, @AssignedBy, SYSUTCDATETIME(), @CreatedBy)",
            assignment,
            cancellationToken: ct));

    public Task RemoveAsync(Guid userId, Guid roleId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security.UserRoles WHERE UserId = @userId AND RoleId = @roleId",
            new { userId, roleId },
            cancellationToken: ct));

    public async Task<(int Added, int Removed)> ReplaceForUserAsync(
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        Guid? assignedBy,
        CancellationToken ct = default)
    {
        // Two-step diff: pull existing IDs, compute add+remove sets,
        // execute both. Could be one MERGE, but the loop keeps the
        // Dapper bindings simple + readable.
        var existing = (await GetRoleIdsByUserAsync(userId, ct)).ToHashSet();
        var desired = roleIds.ToHashSet();

        var toAdd = desired.Except(existing).ToList();
        var toRemove = existing.Except(desired).ToList();

        foreach (var roleId in toAdd)
        {
            await AddAsync(new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = roleId,
                AssignedBy = assignedBy,
                CreatedBy = assignedBy,
            }, ct);
        }

        if (toRemove.Count > 0)
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM security.UserRoles WHERE UserId = @userId AND RoleId IN @roleIds",
                new { userId, roleIds = toRemove },
                cancellationToken: ct));
        }

        return (toAdd.Count, toRemove.Count);
    }
}
