using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Inbound;

public sealed class ReceivingHeaderRepositoryFactory : IReceivingHeaderRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public ReceivingHeaderRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IReceivingHeaderRepository For(Guid tenantId) =>
        new ReceivingHeaderRepository(_connectionFactory.CreateConnection(tenantId));
}
