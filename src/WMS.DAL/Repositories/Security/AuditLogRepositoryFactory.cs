using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Security;

public sealed class AuditLogRepositoryFactory : IAuditLogRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public AuditLogRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IAuditLogRepository For(Guid tenantId) =>
        new AuditLogRepository(_connectionFactory.CreateConnection(tenantId));
}
