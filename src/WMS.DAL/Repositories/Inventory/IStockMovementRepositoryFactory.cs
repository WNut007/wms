namespace WMS.DAL.Repositories.Inventory;

public interface IStockMovementRepositoryFactory
{
    IStockMovementRepository For(Guid tenantId);
}
