namespace WMS.Domain.Entities.Master;

// Phase 27 — maps to master.SuperAdmins. Platform-level operator
// identity, distinct from tenant security.Users. SuperAdmins
// provision and manage tenant lifecycles via /SuperAdmin/.
//
// Schema baseline from Phase 1 Migration_004 (Id / Email / PasswordHash
// / Permissions JSON / IsActive / CreatedAt / LastLoginAt) extended by
// Phase 27 Migration_20260514_011 with FailedLoginAttempts +
// LockedUntil + FullName + MustChangePassword.
//
// Audit fields (UpdatedAt / CreatedBy / UpdatedBy) live on the row but
// note from Migration_009 header: SuperAdmins has NO self-FK on
// CreatedBy / UpdatedBy (bootstrap chicken-and-egg problem) — stored as
// bare Guid, no constraint, no Audit row required for first row.
public sealed class SuperAdmin
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public bool MustChangePassword { get; set; }
    public string? Permissions { get; set; }  // JSON; Phase 27 doesn't use it yet (all SuperAdmins have full access)
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
