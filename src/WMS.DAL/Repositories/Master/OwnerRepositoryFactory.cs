using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class OwnerRepositoryFactory : IOwnerRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public OwnerRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IOwnerRepository For(Guid tenantId) =>
        new OwnerRepository(_connectionFactory.CreateConnection(tenantId));
}
