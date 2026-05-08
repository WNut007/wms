using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class ProductRepositoryFactory : IProductRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public ProductRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IProductRepository For(Guid tenantId) =>
        new ProductRepository(_connectionFactory.CreateConnection(tenantId));
}
