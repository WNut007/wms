namespace WMS.Domain.Entities.Security;

// Maps to security.AuditLog. Append-only — rows are immutable per the
// migration comment ("audit-of-audit is intentionally absent"). No
// UpdatedAt / CreatedBy / UpdatedBy columns. Repository exposes only
// AppendAsync + queries; no UpdateAsync / DeleteAsync exists.
//
// Named AuditLogEntry to avoid clashing with the System.Diagnostics
// AuditLog convention; SQL table stays "AuditLog".
public sealed class AuditLogEntry
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = "";
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
