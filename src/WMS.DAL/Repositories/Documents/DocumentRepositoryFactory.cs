using WMS.Common.Multitenancy;

namespace WMS.DAL.Repositories.Documents;

public sealed class DocumentRepositoryFactory : IDocumentRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public DocumentRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IDocumentRepository For(Guid tenantId) =>
        new DocumentRepository(_connectionFactory.CreateConnection(tenantId));
}
