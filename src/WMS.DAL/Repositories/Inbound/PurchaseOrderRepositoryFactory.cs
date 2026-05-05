using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Inbound;

public sealed class PurchaseOrderRepositoryFactory : IPurchaseOrderRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public PurchaseOrderRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IPurchaseOrderRepository For(Guid tenantId) =>
        new PurchaseOrderRepository(_connectionFactory.CreateConnection(tenantId));
}
