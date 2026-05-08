namespace WMS.DAL.Repositories.Documents;

public interface IDocumentRepositoryFactory
{
    IDocumentRepository For(Guid tenantId);
}
