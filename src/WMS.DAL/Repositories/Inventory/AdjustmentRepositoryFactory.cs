using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Inventory;

public sealed class AdjustmentRepositoryFactory : IAdjustmentRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public AdjustmentRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IAdjustmentRepository For(Guid tenantId) =>
        new AdjustmentRepository(_connectionFactory.CreateConnection(tenantId));
}
