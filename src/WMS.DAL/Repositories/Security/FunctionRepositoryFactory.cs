using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Security;

public sealed class FunctionRepositoryFactory : IFunctionRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public FunctionRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IFunctionRepository For(Guid tenantId) =>
        new FunctionRepository(_connectionFactory.CreateConnection(tenantId));
}
