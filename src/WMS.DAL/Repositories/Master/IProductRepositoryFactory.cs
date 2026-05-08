namespace WMS.DAL.Repositories.Master;

// Resolves an IProductRepository bound to the named tenant's DB
// connection. Mirrors IWarehouseRepositoryFactory's shape.
public interface IProductRepositoryFactory
{
    IProductRepository For(Guid tenantId);
}
