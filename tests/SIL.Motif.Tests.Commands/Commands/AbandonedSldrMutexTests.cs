using SIL.Threading;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins that SIL.WritingSystems survives a process killed while holding its machine-wide SLDR cache mutex,
/// which otherwise broke every writing-system load that followed on the machine, FieldWorks' included.
/// </summary>
public class AbandonedSldrMutexTests
{
    private const string SldrCacheMutexName = "SldrCache";

    [Fact]
    public void TheSldrMutexCanBeTakenAfterItsHolderDiedHoldingIt()
    {
        if (!OperatingSystem.IsWindows()) return;

        var holder = new Thread(AbandonTheMutex);
        holder.Start();
        holder.Join();

        using var mutex = new GlobalMutex(SldrCacheMutexName);
        mutex.Initialize();
        using (mutex.Lock()) { }
    }

    // Owning the mutex when the thread ends is what abandons it; it may already be abandoned on entry.
    private static void AbandonTheMutex()
    {
        var mutex = new Mutex(false, SldrCacheMutexName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
    }
}
