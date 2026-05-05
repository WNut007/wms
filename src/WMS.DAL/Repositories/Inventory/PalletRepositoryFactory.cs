using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Inventory;

public sealed class PalletRepositoryFactory : IPalletRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public PalletRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IPalletRepository For(Guid tenantId) =>
        new PalletRepository(_connectionFactory.CreateConnection(tenantId));
}
