using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class LocationRepositoryFactory : ILocationRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public LocationRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public ILocationRepository For(Guid tenantId) =>
        new LocationRepository(_connectionFactory.CreateConnection(tenantId));
}
