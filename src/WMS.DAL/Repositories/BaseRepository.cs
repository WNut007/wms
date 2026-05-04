using System.Data;
using WMS.Domain.Entities;

namespace WMS.DAL.Repositories;

// TODO: replace IDbConnection ctor with ITenantDbFactory once that
// abstraction lands. Concrete repos use Dapper against `Connection`.
public abstract class BaseRepository<T> : IBaseRepository<T> where T : BaseEntity
{
    protected Guid TenantId { get; }
    protected IDbConnection Connection { get; }

    protected BaseRepository(Guid tenantId, IDbConnection connection)
    {
        TenantId = tenantId;
        Connection = connection;
    }

    public abstract Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    public abstract Task<Guid> AddAsync(T entity, CancellationToken ct = default);
    public abstract Task UpdateAsync(T entity, CancellationToken ct = default);
    public abstract Task DeleteAsync(Guid id, CancellationToken ct = default);
}
