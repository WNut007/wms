using System.Data;
using Dapper;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14B — Dapper-backed OrderAllocation repo. Writes are
// single-statement (no per-method TX); ambient TransactionScope from
// the orchestrating AllocationService binds them all.
internal sealed class OrderAllocationRepository : IOrderAllocationRepository
{
    private const string EntityColumns = @"
        Id, SalesOrderLineId, StockId, AllocatedQuantity, Status,
        AllocatedAt, AllocatedBy,
        ReleasedAt, ReleasedBy, ReleaseReason,
        CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM outbound.OrderAllocations";

    private readonly IDbConnection _connection;

    public OrderAllocationRepository(IDbConnection connection) => _connection = connection;

    public Task CreateAsync(
        OrderAllocation a, Guid? userId, CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO outbound.OrderAllocations
                  (Id, SalesOrderLineId, StockId, AllocatedQuantity,
                   Status, AllocatedBy, CreatedBy)
              VALUES
                  (@Id, @SalesOrderLineId, @StockId, @AllocatedQuantity,
                   'Active', @UserId, @UserId);",
            new
            {
                a.Id, a.SalesOrderLineId, a.StockId, a.AllocatedQuantity,
                UserId = userId,
            },
            cancellationToken: ct));

    public async Task CreateBatchAsync(
        IReadOnlyList<OrderAllocation> allocations,
        Guid? userId,
        CancellationToken ct = default)
    {
        // Caller owns the transaction (TransactionScope from the
        // service). Per-row INSERT — line+strategy combos rarely span
        // more than a handful of Stock rows, so the round-trips are
        // negligible.
        foreach (var a in allocations)
            await CreateAsync(a, userId, ct);
    }

    public async Task<IReadOnlyList<OrderAllocationRow>> GetActiveByLineIdAsync(
        Guid salesOrderLineId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    oa.Id,
    oa.SalesOrderLineId,
    sol.LineNumber,
    oa.StockId,
    loc.Code         AS LocationCode,
    lot.LotNumber    AS LotNumber,
    pal.PalletNumber AS PalletNumber,
    oa.AllocatedQuantity,
    oa.Status,
    oa.AllocatedAt,
    COALESCE(u.FullName, u.Email, 'System') AS AllocatedByName
FROM outbound.OrderAllocations oa
JOIN outbound.SalesOrderLines sol ON sol.Id = oa.SalesOrderLineId
JOIN inventory.Stock          s   ON s.Id   = oa.StockId
JOIN master.Locations         loc ON loc.Id = s.LocationId
LEFT JOIN inventory.Lots      lot ON lot.Id = s.LotId
LEFT JOIN inventory.Pallets   pal ON pal.Id = s.PalletId
LEFT JOIN security.Users      u   ON u.Id   = oa.AllocatedBy
WHERE oa.SalesOrderLineId = @lineId
  AND oa.Status = 'Active'
ORDER BY oa.AllocatedAt;";

        var rows = await _connection.QueryAsync<OrderAllocationRow>(new CommandDefinition(
            sql, new { lineId = salesOrderLineId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<OrderAllocationRow>> GetActiveBySalesOrderIdAsync(
        Guid salesOrderId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    oa.Id,
    oa.SalesOrderLineId,
    sol.LineNumber,
    oa.StockId,
    loc.Code         AS LocationCode,
    lot.LotNumber    AS LotNumber,
    pal.PalletNumber AS PalletNumber,
    oa.AllocatedQuantity,
    oa.Status,
    oa.AllocatedAt,
    COALESCE(u.FullName, u.Email, 'System') AS AllocatedByName
FROM outbound.OrderAllocations oa
JOIN outbound.SalesOrderLines sol ON sol.Id = oa.SalesOrderLineId
JOIN inventory.Stock          s   ON s.Id   = oa.StockId
JOIN master.Locations         loc ON loc.Id = s.LocationId
LEFT JOIN inventory.Lots      lot ON lot.Id = s.LotId
LEFT JOIN inventory.Pallets   pal ON pal.Id = s.PalletId
LEFT JOIN security.Users      u   ON u.Id   = oa.AllocatedBy
WHERE sol.SalesOrderId = @soId
  AND oa.Status = 'Active'
ORDER BY sol.LineNumber, oa.AllocatedAt;";

        var rows = await _connection.QueryAsync<OrderAllocationRow>(new CommandDefinition(
            sql, new { soId = salesOrderId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<OrderAllocation>> GetActiveEntitiesBySalesOrderIdAsync(
        Guid salesOrderId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT " + EntityColumns + @"
WHERE Id IN (
    SELECT oa.Id
    FROM outbound.OrderAllocations oa
    JOIN outbound.SalesOrderLines sol ON sol.Id = oa.SalesOrderLineId
    WHERE sol.SalesOrderId = @soId
      AND oa.Status = 'Active'
);";

        var rows = await _connection.QueryAsync<OrderAllocation>(new CommandDefinition(
            sql, new { soId = salesOrderId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> ReleaseAsync(
        Guid allocationId, string? reason, Guid? userId,
        CancellationToken ct = default)
    {
        const string sql = @"
UPDATE outbound.OrderAllocations
SET Status        = 'Released',
    ReleasedAt    = SYSUTCDATETIME(),
    ReleasedBy    = @UserId,
    ReleaseReason = @Reason,
    UpdatedAt     = SYSUTCDATETIME(),
    UpdatedBy     = @UserId,
    Version       = Version + 1
WHERE Id = @Id AND Status = 'Active';";

        var rows = await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = allocationId, Reason = reason, UserId = userId },
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task<int> ReleaseAllForSalesOrderAsync(
        Guid salesOrderId, string reason, Guid? userId,
        CancellationToken ct = default)
    {
        // Single UPDATE … WHERE Id IN (...) — atomic flip across all
        // Active allocations on the SO's lines. Returns count for the
        // service to log.
        const string sql = @"
UPDATE outbound.OrderAllocations
SET Status        = 'Released',
    ReleasedAt    = SYSUTCDATETIME(),
    ReleasedBy    = @UserId,
    ReleaseReason = @Reason,
    UpdatedAt     = SYSUTCDATETIME(),
    UpdatedBy     = @UserId,
    Version       = Version + 1
WHERE Status = 'Active'
  AND SalesOrderLineId IN (
      SELECT Id FROM outbound.SalesOrderLines WHERE SalesOrderId = @soId
  );";

        return await _connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { soId = salesOrderId, Reason = reason, UserId = userId },
            cancellationToken: ct));
    }
}
