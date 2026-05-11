using System.Data;
using Dapper;
using WMS.DAL.Common;
using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

internal sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbConnection _connection;

    public AuditLogRepository(IDbConnection connection) => _connection = connection;

    public Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO security.AuditLog
                (Id, UserId, EventType, EntityType, EntityId,
                 IpAddress, UserAgent, Details, CreatedAt)
              VALUES
                (@Id, @UserId, @EventType, @EntityType, @EntityId,
                 @IpAddress, @UserAgent, @Details, SYSUTCDATETIME())",
            entry,
            cancellationToken: ct));

    public async Task<PagedResult<AuditLogListRow>> GetPagedAsync(
        AuditLogFilter filter,
        CancellationToken ct = default)
    {
        var offset = (filter.Page - 1) * filter.PageSize;
        const string sql = @"
SELECT a.Id, a.UserId,
       u.Email AS UserEmail,
       u.FullName AS UserFullName,
       a.EventType, a.EntityType, a.EntityId,
       a.IpAddress, a.UserAgent, a.Details, a.CreatedAt
FROM security.AuditLog a
LEFT JOIN security.Users u ON u.Id = a.UserId
WHERE (@userId IS NULL OR a.UserId = @userId)
  AND (@eventType IS NULL OR a.EventType = @eventType)
  AND (@entityType IS NULL OR a.EntityType = @entityType)
  AND (@fromUtc IS NULL OR a.CreatedAt >= @fromUtc)
  AND (@toUtc IS NULL OR a.CreatedAt < @toUtc)
  AND (@search IS NULL
       OR a.EventType LIKE '%' + @search + '%'
       OR a.EntityType LIKE '%' + @search + '%'
       OR u.Email LIKE '%' + @search + '%'
       OR a.Details LIKE '%' + @search + '%')
ORDER BY a.CreatedAt DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

SELECT COUNT(*)
FROM security.AuditLog a
LEFT JOIN security.Users u ON u.Id = a.UserId
WHERE (@userId IS NULL OR a.UserId = @userId)
  AND (@eventType IS NULL OR a.EventType = @eventType)
  AND (@entityType IS NULL OR a.EntityType = @entityType)
  AND (@fromUtc IS NULL OR a.CreatedAt >= @fromUtc)
  AND (@toUtc IS NULL OR a.CreatedAt < @toUtc)
  AND (@search IS NULL
       OR a.EventType LIKE '%' + @search + '%'
       OR a.EntityType LIKE '%' + @search + '%'
       OR u.Email LIKE '%' + @search + '%'
       OR a.Details LIKE '%' + @search + '%');";

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new { userId = filter.UserId, eventType = filter.EventType, entityType = filter.EntityType,
                  fromUtc = filter.FromUtc, toUtc = filter.ToUtc, search = filter.Search,
                  offset, pageSize = filter.PageSize },
            cancellationToken: ct));

        var rows = (await multi.ReadAsync<AuditLogListRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();
        return new PagedResult<AuditLogListRow>
        {
            Items = rows,
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)filter.PageSize),
        };
    }

    public Task<AuditLogListRow?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<AuditLogListRow?>(new CommandDefinition(
            @"SELECT a.Id, a.UserId,
                     u.Email AS UserEmail,
                     u.FullName AS UserFullName,
                     a.EventType, a.EntityType, a.EntityId,
                     a.IpAddress, a.UserAgent, a.Details, a.CreatedAt
              FROM security.AuditLog a
              LEFT JOIN security.Users u ON u.Id = a.UserId
              WHERE a.Id = @id",
            new { id },
            cancellationToken: ct));

    public async Task<IReadOnlyList<string>> GetDistinctEventTypesAsync(CancellationToken ct = default)
    {
        var rows = await _connection.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT EventType FROM security.AuditLog ORDER BY EventType",
            cancellationToken: ct));
        return rows.AsList();
    }
}
