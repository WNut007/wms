using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Inventory;

public sealed class StockMovementRepositoryFactory : IStockMovementRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public StockMovementRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IStockMovementRepository For(Guid tenantId) =>
        new StockMovementRepository(_connectionFactory.CreateConnection(tenantId));
}
