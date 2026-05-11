using Dapper;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

public sealed class SystemAuditLogRepository : ISystemAuditLogRepository
{
    private readonly IMasterConnectionFactory _factory;

    public SystemAuditLogRepository(IMasterConnectionFactory factory) =>
        _factory = factory;

    public async Task AppendAsync(SystemAuditLogEntry entry, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO [master].[SystemAuditLog]
                (Id, EventType, Severity, UserId, UserEmail, TenantId,
                 EntityType, EntityId, Details, IpAddress, Timestamp)
              VALUES
                (@Id, @EventType, @Severity, @UserId, @UserEmail, @TenantId,
                 @EntityType, @EntityId, @Details, @IpAddress, SYSUTCDATETIME())",
            entry,
            cancellationToken: ct));
    }

    public async Task<PagedResult<SystemAuditLogEntry>> GetPagedAsync(
        SystemAuditLogFilter filter,
        CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        var offset = (filter.Page - 1) * filter.PageSize;

        const string sql = @"
SELECT Id, EventType, Severity, UserId, UserEmail, TenantId,
       EntityType, EntityId, Details, IpAddress, Timestamp
FROM [master].[SystemAuditLog]
WHERE (@eventType IS NULL OR EventType = @eventType)
  AND (@tenantId IS NULL OR TenantId = @tenantId)
  AND (@userId IS NULL OR UserId = @userId)
  AND (@fromUtc IS NULL OR Timestamp >= @fromUtc)
  AND (@toUtc IS NULL OR Timestamp < @toUtc)
  AND (@search IS NULL
       OR EventType LIKE '%' + @search + '%'
       OR UserEmail LIKE '%' + @search + '%'
       OR Details LIKE '%' + @search + '%')
ORDER BY Timestamp DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

SELECT COUNT(*)
FROM [master].[SystemAuditLog]
WHERE (@eventType IS NULL OR EventType = @eventType)
  AND (@tenantId IS NULL OR TenantId = @tenantId)
  AND (@userId IS NULL OR UserId = @userId)
  AND (@fromUtc IS NULL OR Timestamp >= @fromUtc)
  AND (@toUtc IS NULL OR Timestamp < @toUtc)
  AND (@search IS NULL
       OR EventType LIKE '%' + @search + '%'
       OR UserEmail LIKE '%' + @search + '%'
       OR Details LIKE '%' + @search + '%');";

        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            sql,
            new { eventType = filter.EventType, tenantId = filter.TenantId, userId = filter.UserId,
                  fromUtc = filter.FromUtc, toUtc = filter.ToUtc, search = filter.Search,
                  offset, pageSize = filter.PageSize },
            cancellationToken: ct));

        var rows = (await multi.ReadAsync<SystemAuditLogEntry>()).AsList();
        var total = await multi.ReadSingleAsync<int>();
        return new PagedResult<SystemAuditLogEntry>
        {
            Items = rows,
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)filter.PageSize),
        };
    }
}
