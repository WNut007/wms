using WMS.Domain.Entities.Master;

namespace WMS.DAL.Repositories.Master;

// Phase 27 — master.SuperAdmins repository. Used by the SuperAdmin auth
// service + the first-run seeder. Lives on the Master DB connection
// (singleton, fixed connection string from config).
public interface ISuperAdminRepository
{
    Task<SuperAdmin?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<SuperAdmin?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SuperAdmin>> GetAllAsync(CancellationToken ct = default);

    // Bootstrap seed path. Idempotent on Email (Unique). Returns the
    // resolved Id (existing row's or the newly-inserted one's).
    Task<Guid> UpsertByEmailAsync(SuperAdmin entity, CancellationToken ct = default);

    Task UpdateLastLoginAsync(Guid id, DateTime utcNow, CancellationToken ct = default);
    Task IncrementFailedLoginAsync(Guid id, CancellationToken ct = default);
    Task SetLockedUntilAsync(Guid id, DateTime lockedUntilUtc, CancellationToken ct = default);
    Task ResetFailedLoginAsync(Guid id, CancellationToken ct = default);

    Task UpdatePasswordHashAsync(
        Guid id,
        string newPasswordHash,
        bool mustChangePassword,
        Guid? actorId,
        CancellationToken ct = default);
}
