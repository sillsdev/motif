using SIL.Motif.Host.LcmUtils;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public class SldrCacheMutexTests
{
    [Fact]
    public void AMutexLeftByAKilledHolderNoLongerThrowsAfterItIsReclaimed()
    {
        if (!OperatingSystem.IsWindows()) return;

        var name = $"MotifSldrCacheTest-{Guid.NewGuid():N}";
        var holder = new Thread(() => AbandonTheMutex(name));
        holder.Start();
        holder.Join();

        SldrCacheMutex.ReclaimIfAbandoned(name);

        using var mutex = new Mutex(false, name);
        var acquired = mutex.WaitOne(TimeSpan.FromSeconds(30));
        Assert.True(acquired);
        mutex.ReleaseMutex();
    }

    // Owning the mutex when the thread ends is what abandons it; it may already be abandoned on entry.
    private static void AbandonTheMutex(string name)
    {
        var mutex = new Mutex(false, name);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
    }
}
