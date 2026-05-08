namespace WMS.DAL.Repositories.Master;

// Resolves an ICustomerRepository bound to the named tenant's DB
// connection. Mirrors IWarehouseRepositoryFactory's shape.
public interface ICustomerRepositoryFactory
{
    ICustomerRepository For(Guid tenantId);
}
