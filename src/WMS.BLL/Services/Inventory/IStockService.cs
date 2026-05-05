using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inventory;

// Read-only stock queries — mutation primitives (receive, putaway,
// adjust, reserve, pick) live on dedicated services per ADR. Every
// method takes an explicit tenantId so background jobs (no
// HttpContext) can call without a TenantContext available.
public interface IStockService
{
    Task<Stock?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Stock>> GetByLocationAsync(
        Guid tenantId, Guid locationId, CancellationToken ct = default);

    Task<IReadOnlyList<Stock>> GetByProductAsync(
        Guid tenantId, Guid productId, CancellationToken ct = default);

    // NULL-safe lookup on the 6-tuple — the upsert primitive that
    // future receive / putaway flows lean on.
    Task<Stock?> GetByKeyAsync(
        Guid tenantId, StockKey key, CancellationToken ct = default);

    // OnHand minus Allocated, only rows where that's strictly positive.
    // Drives ATP and pickable-stock reports.
    Task<IReadOnlyList<Stock>> GetAvailableByProductAsync(
        Guid tenantId, Guid productId, CancellationToken ct = default);
}
