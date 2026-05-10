using Moq;
using WMS.BLL.Strategies.Allocation;

namespace WMS.UnitTests.Strategies.Allocation;

// Phase 14B — resolver behavior tests. Strategy registration sanity
// at construction time + lookup-by-name correctness at Resolve.
public class AllocationStrategyResolverTests
{
    private static Mock<IAllocationStrategy> Stub(string name)
    {
        var m = new Mock<IAllocationStrategy>();
        m.SetupGet(s => s.Name).Returns(name);
        return m;
    }

    [Fact]
    public void Ctor_MissingFifo_Throws()
    {
        var foo = Stub("Foo").Object;
        Assert.Throws<InvalidOperationException>(
            () => new AllocationStrategyResolver(new[] { foo }));
    }

    [Fact]
    public void Resolve_NullName_ReturnsDefaultFifo()
    {
        var fifo = Stub("FIFO").Object;
        var resolver = new AllocationStrategyResolver(new[] { fifo });

        Assert.Same(fifo, resolver.Resolve(null));
    }

    [Fact]
    public void Resolve_EmptyName_ReturnsDefaultFifo()
    {
        var fifo = Stub("FIFO").Object;
        var resolver = new AllocationStrategyResolver(new[] { fifo });

        Assert.Same(fifo, resolver.Resolve(""));
        Assert.Same(fifo, resolver.Resolve("  "));
    }

    [Fact]
    public void Resolve_KnownName_CaseInsensitive()
    {
        var fifo = Stub("FIFO").Object;
        var fefo = Stub("FEFO").Object;
        var resolver = new AllocationStrategyResolver(new[] { fifo, fefo });

        Assert.Same(fefo, resolver.Resolve("FEFO"));
        Assert.Same(fefo, resolver.Resolve("fefo"));
        Assert.Same(fefo, resolver.Resolve("Fefo"));
    }

    [Fact]
    public void Resolve_UnknownName_Throws()
    {
        var fifo = Stub("FIFO").Object;
        var resolver = new AllocationStrategyResolver(new[] { fifo });

        var ex = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve("Unknown"));
        Assert.Contains("Unknown", ex.Message);
        Assert.Contains("FIFO", ex.Message); // lists registered
    }
}
