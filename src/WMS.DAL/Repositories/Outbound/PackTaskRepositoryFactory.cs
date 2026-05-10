using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Outbound;

public sealed class PackTaskRepositoryFactory : IPackTaskRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public PackTaskRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IPackTaskRepository For(Guid tenantId) =>
        new PackTaskRepository(_connectionFactory.CreateConnection(tenantId));
}
