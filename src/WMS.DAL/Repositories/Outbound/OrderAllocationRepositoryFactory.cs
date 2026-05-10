using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Outbound;

public sealed class OrderAllocationRepositoryFactory : IOrderAllocationRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public OrderAllocationRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IOrderAllocationRepository For(Guid tenantId) =>
        new OrderAllocationRepository(_connectionFactory.CreateConnection(tenantId));
}
