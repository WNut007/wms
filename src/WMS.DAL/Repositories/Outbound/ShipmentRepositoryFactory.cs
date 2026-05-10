using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Outbound;

public sealed class ShipmentRepositoryFactory : IShipmentRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public ShipmentRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IShipmentRepository For(Guid tenantId) =>
        new ShipmentRepository(_connectionFactory.CreateConnection(tenantId));
}
