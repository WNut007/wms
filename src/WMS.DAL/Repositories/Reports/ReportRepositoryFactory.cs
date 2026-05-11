using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Reports;

public sealed class ReportRepositoryFactory : IReportRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public ReportRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IReportRepository For(Guid tenantId) =>
        new ReportRepository(_connectionFactory.CreateConnection(tenantId));
}
