using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Outbound;

public sealed class SalesOrderRepositoryFactory : ISalesOrderRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public SalesOrderRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public ISalesOrderRepository For(Guid tenantId) =>
        new SalesOrderRepository(_connectionFactory.CreateConnection(tenantId));
}
