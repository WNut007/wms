namespace WMS.DAL.Repositories.Inbound;

public interface IPurchaseOrderRepositoryFactory
{
    IPurchaseOrderRepository For(Guid tenantId);
}
