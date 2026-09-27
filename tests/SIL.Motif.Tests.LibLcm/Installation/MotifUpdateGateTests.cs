using SIL.Motif.Host.Installation;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Installation;

public sealed class MotifUpdateGateTests
{
    [Fact]
    public void OnlyOneActivityCanHoldTheUpdateGateAtATime()
    {
        var mutexName = "SIL.Motif.UpdateActivity.Test." + Guid.NewGuid().ToString("N");
        using var first = MotifUpdateGate.TryAcquire(mutexName);
        Assert.NotNull(first);
        Assert.Null(MotifUpdateGate.TryAcquire(mutexName));

        first!.Dispose();
        using var next = MotifUpdateGate.TryAcquire(mutexName);
        Assert.NotNull(next);
    }
}
