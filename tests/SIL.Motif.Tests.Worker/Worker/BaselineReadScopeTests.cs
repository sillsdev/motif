using SIL.Motif.Host.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Pins that reads of one command share a single Baseline copy and that nothing outside a scope does.</summary>
public sealed class BaselineReadScopeTests
{
    [Fact]
    public void WithoutAScopeEveryReadOpensItsOwnCopy()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "none.fwdata");
        Assert.ThrowsAny<Exception>(() => BaselineReadScope.Open(missing));
    }

    [Fact]
    public void ScopesNestAndTheOuterOneOwnsTheCopy()
    {
        using var outer = BaselineReadScope.Begin();
        using (BaselineReadScope.Begin()) { }
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "none.fwdata");
        Assert.ThrowsAny<Exception>(() => BaselineReadScope.Open(missing));
    }
}
