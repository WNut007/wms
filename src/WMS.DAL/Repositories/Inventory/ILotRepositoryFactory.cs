namespace WMS.DAL.Repositories.Inventory;

// Resolves an ILotRepository bound to the named tenant's DB connection.
public interface ILotRepositoryFactory
{
    ILotRepository For(Guid tenantId);
}
