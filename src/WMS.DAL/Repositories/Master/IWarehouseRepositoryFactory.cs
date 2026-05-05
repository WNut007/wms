namespace WMS.DAL.Repositories.Master;

// Resolves an IWarehouseRepository bound to the named tenant's DB
// connection. Mirrors IUserRepositoryFactory's shape.
public interface IWarehouseRepositoryFactory
{
    IWarehouseRepository For(Guid tenantId);
}
