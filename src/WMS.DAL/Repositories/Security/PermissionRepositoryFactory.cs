using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Security;

public sealed class PermissionRepositoryFactory : IPermissionRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public PermissionRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IPermissionRepository For(Guid tenantId) =>
        new PermissionRepository(_connectionFactory.CreateConnection(tenantId));
}
