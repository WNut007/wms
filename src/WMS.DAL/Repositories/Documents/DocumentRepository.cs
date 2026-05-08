using System.Data;
using Dapper;
using WMS.Domain.Entities.Documents;

namespace WMS.DAL.Repositories.Documents;

// Dapper-backed reader/writer for documents.Files. Bound to a single
// tenant DB connection in its ctor; the factory creates one per
// tenantId via ITenantConnectionFactory. Mirrors WarehouseRepository's
// shape.
internal sealed class DocumentRepository : IDocumentRepository
{
    private readonly IDbConnection _connection;

    public DocumentRepository(IDbConnection connection) => _connection = connection;

    public async Task<Guid> InsertAsync(DocumentFile e, CancellationToken ct = default)
    {
        // Id defaults via NEWID() at the DB if the caller leaves it empty;
        // we always pre-assign so the storage service can use the same
        // Guid for the on-disk filename without a follow-up read.
        if (e.Id == Guid.Empty) e.Id = Guid.NewGuid();
        if (e.CreatedAt == default) e.CreatedAt = DateTime.UtcNow;

        const string sql = @"
INSERT INTO [documents].[Files]
    (Id, EntityType, EntityId, FileName, ContentType, FileSize, Extension,
     Category, StorageProvider, StorageKey, IsArchived,
     CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version)
VALUES
    (@Id, @EntityType, @EntityId, @FileName, @ContentType, @FileSize, @Extension,
     @Category, @StorageProvider, @StorageKey, @IsArchived,
     @CreatedAt, @UpdatedAt, @CreatedBy, @UpdatedBy, @Version);";

        await _connection.ExecuteAsync(new CommandDefinition(sql, e, cancellationToken: ct));
        return e.Id;
    }

    public Task<DocumentFile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QueryFirstOrDefaultAsync<DocumentFile>(new CommandDefinition(
            "SELECT * FROM [documents].[Files] WHERE Id = @id AND IsArchived = 0",
            new { id },
            cancellationToken: ct));

    public async Task<IReadOnlyList<DocumentFile>> GetByEntityAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default)
    {
        // Filtered index IX_Files_Entity covers this exactly — every
        // Detail page hits this path on load.
        var rows = await _connection.QueryAsync<DocumentFile>(new CommandDefinition(
            @"SELECT *
              FROM [documents].[Files]
              WHERE EntityType = @entityType
                AND EntityId = @entityId
                AND IsArchived = 0
              ORDER BY CreatedAt DESC",
            new { entityType, entityId },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM [documents].[Files] WHERE Id = @id",
            new { id },
            cancellationToken: ct));
        return rows > 0;
    }
}
