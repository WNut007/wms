using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Master;

public sealed class BoxTypeRepositoryFactory : IBoxTypeRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public BoxTypeRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IBoxTypeRepository For(Guid tenantId) =>
        new BoxTypeRepository(_connectionFactory.CreateConnection(tenantId));
}
