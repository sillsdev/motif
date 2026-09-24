using SIL.Threading;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins that SIL.Core's <see cref="GlobalMutex"/>, which guards SIL.WritingSystems' machine-wide SLDR cache,
/// survives a holder that died holding it. Before libpalaso 18 that broke every writing-system load that
/// followed on the machine, FieldWorks' included.
/// </summary>
/// <remarks>
/// Uses a private name: abandoning the real SLDR cache mutex would hang other processes still on an older
/// libpalaso.
/// </remarks>
public class AbandonedSldrMutexTests
{
    [Fact]
    public void AGlobalMutexCanBeTakenAfterItsHolderDiedHoldingIt()
    {
        if (!OperatingSystem.IsWindows()) return;

        var name = $"MotifAbandonedMutexTest-{Guid.NewGuid():N}";
        var holder = new Thread(() => AbandonTheMutex(name));
        holder.Start();
        holder.Join();

        using var mutex = new GlobalMutex(name);
        mutex.Initialize();
        using (mutex.Lock()) { }
    }

    // Owning the mutex when the thread ends is what abandons it.
    private static void AbandonTheMutex(string name)
    {
        var mutex = new Mutex(false, name);
        mutex.WaitOne();
    }
}
