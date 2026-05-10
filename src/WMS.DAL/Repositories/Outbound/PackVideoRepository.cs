using System.Data;
using Dapper;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

internal sealed class PackVideoRepository : IPackVideoRepository
{
    private const string Columns = @"
        Id, PackTaskId, DocumentFileId, DurationSec,
        RecordedAt, RecordedBy,
        CreatedAt, CreatedBy
        FROM outbound.PackVideos";

    private readonly IDbConnection _connection;

    public PackVideoRepository(IDbConnection connection) => _connection = connection;

    public Task CreateAsync(
        PackVideo v, Guid? userId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO outbound.PackVideos
                  (Id, PackTaskId, DocumentFileId, DurationSec,
                   RecordedBy, CreatedBy)
              VALUES
                  (@Id, @PackTaskId, @DocumentFileId, @DurationSec,
                   @UserId, @UserId);",
            new
            {
                v.Id, v.PackTaskId, v.DocumentFileId, v.DurationSec,
                UserId = userId,
            },
            cancellationToken: ct));

    public async Task<PackVideo?> GetLatestByPackTaskAsync(
        Guid packTaskId, CancellationToken ct = default)
    {
        // IX_PackVideos_PackTask covers WHERE+ORDER exactly.
        const string sql = @"
SELECT TOP (1) " + Columns + @"
WHERE PackTaskId = @packTaskId
ORDER BY RecordedAt DESC;";

        return await _connection.QuerySingleOrDefaultAsync<PackVideo?>(
            new CommandDefinition(sql, new { packTaskId }, cancellationToken: ct));
    }

    public Task<PackVideo?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<PackVideo?>(new CommandDefinition(
            "SELECT " + Columns + " WHERE Id = @id;",
            new { id }, cancellationToken: ct));

    public async Task<IReadOnlyList<RetainedVideoRow>> GetOlderThanAsync(
        DateTime cutoff, CancellationToken ct = default)
    {
        // IX_PackVideos_RecordedAt covers the WHERE clause.
        const string sql = @"
SELECT Id, DocumentFileId, RecordedAt
FROM outbound.PackVideos
WHERE RecordedAt < @cutoff
ORDER BY RecordedAt;";

        var rows = await _connection.QueryAsync<RetainedVideoRow>(new CommandDefinition(
            sql, new { cutoff }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM outbound.PackVideos WHERE Id = @id;",
            new { id }, cancellationToken: ct));
}
