using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Inventory;

public sealed class TransferOrderRepositoryFactory : ITransferOrderRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public TransferOrderRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public ITransferOrderRepository For(Guid tenantId) =>
        new TransferOrderRepository(_connectionFactory.CreateConnection(tenantId));
}
