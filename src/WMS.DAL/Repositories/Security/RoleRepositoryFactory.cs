using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Security;

public sealed class RoleRepositoryFactory : IRoleRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public RoleRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IRoleRepository For(Guid tenantId) =>
        new RoleRepository(_connectionFactory.CreateConnection(tenantId));
}
