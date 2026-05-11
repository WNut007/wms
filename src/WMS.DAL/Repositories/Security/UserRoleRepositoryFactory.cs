using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Security;

public sealed class UserRoleRepositoryFactory : IUserRoleRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public UserRoleRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IUserRoleRepository For(Guid tenantId) =>
        new UserRoleRepository(_connectionFactory.CreateConnection(tenantId));
}
