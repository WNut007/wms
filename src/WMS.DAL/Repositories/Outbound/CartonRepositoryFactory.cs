using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Outbound;

public sealed class CartonRepositoryFactory : ICartonRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public CartonRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public ICartonRepository For(Guid tenantId) =>
        new CartonRepository(_connectionFactory.CreateConnection(tenantId));
}
