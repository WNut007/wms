using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Outbound;

public sealed class PickTaskRepositoryFactory : IPickTaskRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public PickTaskRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IPickTaskRepository For(Guid tenantId) =>
        new PickTaskRepository(_connectionFactory.CreateConnection(tenantId));
}
