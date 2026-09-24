using SIL.Motif.Host.LcmUtils;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public class SldrCacheMutexTests
{
    [Fact]
    public void AMutexLeftByAKilledHolderNoLongerThrowsAfterItIsReclaimed()
    {
        if (!OperatingSystem.IsWindows()) return;

        var holder = new Thread(AbandonTheMutex);
        holder.Start();
        holder.Join();

        SldrCacheMutex.ReclaimIfAbandoned();

        using var mutex = new Mutex(false, SldrCacheMutex.Name);
        var acquired = mutex.WaitOne(TimeSpan.FromSeconds(30));
        Assert.True(acquired);
        mutex.ReleaseMutex();
    }

    // Owning the mutex when the thread ends is what abandons it; it may already be abandoned on entry.
    private static void AbandonTheMutex()
    {
        var mutex = new Mutex(false, SldrCacheMutex.Name);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
    }
}
