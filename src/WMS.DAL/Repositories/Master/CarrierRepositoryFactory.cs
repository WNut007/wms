using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class CarrierRepositoryFactory : ICarrierRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public CarrierRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public ICarrierRepository For(Guid tenantId) =>
        new CarrierRepository(_connectionFactory.CreateConnection(tenantId));
}
