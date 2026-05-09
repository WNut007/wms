using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class ProductCategoryRepositoryFactory : IProductCategoryRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public ProductCategoryRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IProductCategoryRepository For(Guid tenantId) =>
        new ProductCategoryRepository(_connectionFactory.CreateConnection(tenantId));
}
