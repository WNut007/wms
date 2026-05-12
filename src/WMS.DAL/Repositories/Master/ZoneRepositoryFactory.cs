using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class ZoneRepositoryFactory : IZoneRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public ZoneRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IZoneRepository For(Guid tenantId) =>
        new ZoneRepository(_connectionFactory.CreateConnection(tenantId));
}
