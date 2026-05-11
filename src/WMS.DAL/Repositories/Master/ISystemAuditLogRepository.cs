namespace WMS.DAL.Repositories.Master;

// Phase 27 — write + read API for master.SystemAuditLog. Cross-tenant
// audit trail emitted by SuperAdmin operations (tenant create / suspend
// / reactivate / password-reset) AND by SuperAdmin auth events (login
// success/failure/lockout).
//
// Rows are immutable per Migration_007 ("No foreign keys: audit rows
// must remain readable even after the referenced Tenant/User/Entity
// rows are deleted (immutability)"). AppendAsync is the only write.
public sealed record SystemAuditLogEntry(
    Guid Id,
    string EventType,
    string Severity,           // 'Info' | 'Warning' | 'Error'
    Guid? UserId,
    string? UserEmail,
    Guid? TenantId,
    string? EntityType,
    Guid? EntityId,
    string? Details,
    string? IpAddress,
    DateTime Timestamp);

public sealed record SystemAuditLogFilter(
    int Page = 1,
    int PageSize = 50,
    string? EventType = null,
    Guid? TenantId = null,
    Guid? UserId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Search = null);

public interface ISystemAuditLogRepository
{
    Task AppendAsync(SystemAuditLogEntry entry, CancellationToken ct = default);

    Task<WMS.DAL.Common.PagedResult<SystemAuditLogEntry>> GetPagedAsync(
        SystemAuditLogFilter filter,
        CancellationToken ct = default);
}
