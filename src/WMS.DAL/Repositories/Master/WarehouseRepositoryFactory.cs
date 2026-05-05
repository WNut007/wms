using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class WarehouseRepositoryFactory : IWarehouseRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public WarehouseRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IWarehouseRepository For(Guid tenantId) =>
        new WarehouseRepository(_connectionFactory.CreateConnection(tenantId));
}
