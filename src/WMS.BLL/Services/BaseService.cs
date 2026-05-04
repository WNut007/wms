using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories;
using WMS.Domain.Entities;

namespace WMS.BLL.Services;

public abstract class BaseService<T> : IBaseService<T> where T : BaseEntity
{
    protected IBaseRepository<T> Repository { get; }
    protected ILogger<BaseService<T>> Logger { get; }

    protected BaseService(IBaseRepository<T> repository, ILogger<BaseService<T>> logger)
    {
        Repository = repository;
        Logger = logger;
    }

    public virtual Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Repository.GetByIdAsync(id, ct);

    public virtual Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        Repository.GetAllAsync(ct);

    public virtual async Task<Guid> CreateAsync(T entity, CancellationToken ct = default)
    {
        await ValidateAsync(entity, ct);
        return await Repository.AddAsync(entity, ct);
    }

    public virtual async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        await ValidateAsync(entity, ct);
        await Repository.UpdateAsync(entity, ct);
    }

    public virtual Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        Repository.DeleteAsync(id, ct);

    // Override to enforce business validation. Throw on invalid input.
    // FluentValidation will plug in here in a later chunk.
    protected virtual Task ValidateAsync(T entity, CancellationToken ct = default) =>
        Task.CompletedTask;
}
