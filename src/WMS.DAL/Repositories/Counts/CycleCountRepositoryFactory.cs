using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Counts;

public sealed class CycleCountRepositoryFactory : ICycleCountRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public CycleCountRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public ICycleCountRepository For(Guid tenantId) =>
        new CycleCountRepository(_connectionFactory.CreateConnection(tenantId));
}
