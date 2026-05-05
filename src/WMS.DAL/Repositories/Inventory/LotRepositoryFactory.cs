using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Inventory;

public sealed class LotRepositoryFactory : ILotRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public LotRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public ILotRepository For(Guid tenantId) =>
        new LotRepository(_connectionFactory.CreateConnection(tenantId));
}
