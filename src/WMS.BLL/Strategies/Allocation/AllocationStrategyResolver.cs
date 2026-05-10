namespace WMS.BLL.Strategies.Allocation;

// Phase 14B — strategy resolver per ADR-005. Receives every
// registered IAllocationStrategy from DI (auto-discovered) and looks
// them up by Name. Service code never reaches for a concrete strategy
// directly.
//
// Default strategy: 'FIFO' for MVP. A future Phase will introduce a
// per-tenant / per-customer-tier configuration that influences the
// requested name; until then the resolver always returns FIFO when
// nothing is specified.
public interface IAllocationStrategyResolver
{
    // Looks up by Name (case-insensitive). When null/empty, returns
    // the default. Throws InvalidOperationException when the requested
    // name is unknown — strategies are configured up-front; an unknown
    // name is a programming error, not a runtime user-input case.
    IAllocationStrategy Resolve(string? strategyName = null);
}

public sealed class AllocationStrategyResolver : IAllocationStrategyResolver
{
    private const string DefaultStrategyName = "FIFO";

    private readonly Dictionary<string, IAllocationStrategy> _byName;

    public AllocationStrategyResolver(IEnumerable<IAllocationStrategy> strategies)
    {
        _byName = strategies.ToDictionary(
            s => s.Name,
            s => s,
            StringComparer.OrdinalIgnoreCase);

        if (!_byName.ContainsKey(DefaultStrategyName))
            throw new InvalidOperationException(
                $"AllocationStrategyResolver requires a strategy named '{DefaultStrategyName}' " +
                $"to be registered as the MVP default.");
    }

    public IAllocationStrategy Resolve(string? strategyName = null)
    {
        var name = string.IsNullOrWhiteSpace(strategyName)
            ? DefaultStrategyName
            : strategyName;

        if (_byName.TryGetValue(name, out var strategy))
            return strategy;

        throw new InvalidOperationException(
            $"Unknown allocation strategy '{name}'. Registered: " +
            $"{string.Join(", ", _byName.Keys)}.");
    }
}
