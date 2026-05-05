using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Security;

public sealed class UserRepositoryFactory : IUserRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public UserRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IUserRepository For(Guid tenantId) =>
        new UserRepository(_connectionFactory.CreateConnection(tenantId));
}
