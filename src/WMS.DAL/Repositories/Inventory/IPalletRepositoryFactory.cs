namespace WMS.DAL.Repositories.Inventory;

public interface IPalletRepositoryFactory
{
    IPalletRepository For(Guid tenantId);
}
