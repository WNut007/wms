using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Outbound;

public sealed class PackVideoRepositoryFactory : IPackVideoRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public PackVideoRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IPackVideoRepository For(Guid tenantId) =>
        new PackVideoRepository(_connectionFactory.CreateConnection(tenantId));
}
