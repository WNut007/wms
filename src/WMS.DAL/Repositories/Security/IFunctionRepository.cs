using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

// Read-only — Functions are seeded by migrations and don't get edited
// at runtime. Returns rows sorted by (Module, DisplayOrder) so the
// permission-matrix view groups naturally.
public interface IFunctionRepository
{
    Task<IReadOnlyList<Function>> GetAllActiveAsync(CancellationToken ct = default);
    Task<Function?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
