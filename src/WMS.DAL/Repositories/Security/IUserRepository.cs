using WMS.DAL.Common;
using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

// Tenant-scoped reads + writes against security.Users. The repository
// is bound to a single tenant DB connection — get an instance via
// IUserRepositoryFactory rather than newing one up.
public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpdateLastLoginAsync(Guid userId, DateTime utcNow, CancellationToken ct = default);
    Task IncrementFailedLoginAsync(Guid userId, CancellationToken ct = default);
    Task ResetFailedLoginAsync(Guid userId, CancellationToken ct = default);

    // Phase 24 — admin CRUD surface.
    Task<PagedResult<UserListRow>> GetPagedAsync(UserFilter filter, CancellationToken ct = default);
    Task<UserStatusCounts> GetStatusCountsAsync(UserFilter filter, CancellationToken ct = default);

    // Returns true if any user other than @exceptId has the email.
    // exceptId=null on create path; populated on edit so the row being
    // edited doesn't collide with itself.
    Task<bool> EmailExistsAsync(string email, Guid? exceptId, CancellationToken ct = default);

    Task InsertAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);

    // Admin-driven IsActive toggle. Soft-delete pattern — never hard-
    // deletes (preserves FK refs from AuditLog + UserRoles). Returns
    // true on actual flip; false if the row was already at target state
    // (idempotent). Sets UpdatedBy/At inside.
    Task<bool> SetActiveAsync(Guid userId, bool isActive, Guid? actorId, CancellationToken ct = default);

    // For the "last ADMIN can't be deactivated" invariant.
    Task<int> CountActiveAdminsAsync(string adminRoleCode, CancellationToken ct = default);

    // Phase 25 — admin reset + self-change both update PasswordHash. Pure
    // hash UPDATE (no other column changes); audit row written by the
    // service caller so the storage layer stays simple.
    Task UpdatePasswordHashAsync(
        Guid userId,
        string newPasswordHash,
        Guid? actorId,
        CancellationToken ct = default);

    // Phase 25 — explicit lockout stamp used by AuthService when the
    // failed-login threshold is hit. UpdateLastLoginAsync + Reset already
    // clear the counter on success; this method is the inverse: stamp
    // LockedUntil + leave FailedLoginAttempts alone (the count is the
    // proof of the lockout trigger and survives for audit).
    Task SetLockedUntilAsync(
        Guid userId,
        DateTime lockedUntilUtc,
        CancellationToken ct = default);
}
