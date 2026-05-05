using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Inventory;

public sealed class StockRepositoryFactory : IStockRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public StockRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IStockRepository For(Guid tenantId) =>
        new StockRepository(_connectionFactory.CreateConnection(tenantId));
}
