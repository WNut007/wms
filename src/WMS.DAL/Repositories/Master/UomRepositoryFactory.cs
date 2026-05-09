using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class UomRepositoryFactory : IUomRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public UomRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IUomRepository For(Guid tenantId) =>
        new UomRepository(_connectionFactory.CreateConnection(tenantId));
}
