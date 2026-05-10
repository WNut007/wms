namespace WMS.DAL.Repositories.Outbound;

public interface ISalesOrderRepositoryFactory
{
    ISalesOrderRepository For(Guid tenantId);
}
