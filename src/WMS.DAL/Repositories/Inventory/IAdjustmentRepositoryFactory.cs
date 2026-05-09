namespace WMS.DAL.Repositories.Inventory;

public interface IAdjustmentRepositoryFactory
{
    IAdjustmentRepository For(Guid tenantId);
}
