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
}
