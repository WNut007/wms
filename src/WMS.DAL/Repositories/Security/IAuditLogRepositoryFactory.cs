namespace WMS.DAL.Repositories.Security;

public interface IAuditLogRepositoryFactory
{
    IAuditLogRepository For(Guid tenantId);
}
