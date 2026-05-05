namespace WMS.DAL.Repositories.Inventory;

// Resolves an IStockRepository bound to the named tenant's DB
// connection. Mirrors IUserRepositoryFactory's shape.
public interface IStockRepositoryFactory
{
    IStockRepository For(Guid tenantId);
}
