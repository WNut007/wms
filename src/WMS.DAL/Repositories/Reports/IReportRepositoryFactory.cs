namespace WMS.DAL.Repositories.Reports;

// Resolves an IReportRepository bound to the named tenant's DB
// connection. Mirrors IStockRepositoryFactory's shape.
public interface IReportRepositoryFactory
{
    IReportRepository For(Guid tenantId);
}
