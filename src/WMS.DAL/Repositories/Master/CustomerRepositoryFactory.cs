using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class CustomerRepositoryFactory : ICustomerRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public CustomerRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public ICustomerRepository For(Guid tenantId) =>
        new CustomerRepository(_connectionFactory.CreateConnection(tenantId));
}
